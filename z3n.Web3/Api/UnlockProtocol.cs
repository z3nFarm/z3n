using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZennoLab.InterfacesLibrary.Enums.Log;
using ZennoLab.InterfacesLibrary.ProjectModel;
using Newtonsoft.Json.Linq;
using Nethereum.Web3.Accounts;

using System.Net.Http;
using Nethereum.Signer;
namespace z3nCore
{
    public class Unlock
    {
        private readonly string _privateKey; // Добавь в конструктор

        protected readonly IZennoPosterProjectModel _project;
        protected readonly string _jsonRpc;
        protected readonly Blockchain _blockchain;

        protected readonly string _abi = @"[
                        {
                            ""inputs"": [
                            {
                                ""internalType"": ""uint256"",
                                ""name"": ""_tokenId"",
                                ""type"": ""uint256""
                            }
                            ],
                            ""name"": ""keyExpirationTimestampFor"",
                            ""outputs"": [
                            {
                                ""internalType"": ""uint256"",
                                ""name"": """",
                                ""type"": ""uint256""
                            }
                            ],
                            ""stateMutability"": ""view"",
                            ""type"": ""function""
                        },
                        {
                            ""inputs"": [
                            {
                                ""internalType"": ""uint256"",
                                ""name"": ""_tokenId"",
                                ""type"": ""uint256""
                            }
                            ],
                            ""name"": ""ownerOf"",
                            ""outputs"": [
                            {
                                ""internalType"": ""address"",
                                ""name"": """",
                                ""type"": ""address""
                            }
                            ],
                            ""stateMutability"": ""view"",
                            ""type"": ""function""
                        }
                    ]";

        private readonly Logger _logger;
        private const string AUTH_BASE = "https://locksmith.unlock-protocol.com/v2/auth";

        private const string BASE_URL = "https://locksmith.unlock-protocol.com/v2/api/metadata";
        private string _bearerToken;


        public Unlock(IZennoPosterProjectModel project, bool log = false,string privateKey = null)
        {
            _project = project;
            _jsonRpc = Rpc.Base;
            _blockchain = new Blockchain(_jsonRpc);
            _logger = new Logger(project, log: log, classEmoji: "🔓");
            _privateKey = privateKey;
        }

        public string GetKeyMetadata(int chainId, string lockAddress, int tokenId)
        {
            string url = $"{BASE_URL}/{chainId}/locks/{lockAddress}/keys/{tokenId}";
            _logger.Send($"Fetching metadata for key {tokenId} on lock {lockAddress}");

            string response = _project.GET(url, parse: false);
            return response;
        }

        public string keyExpirationTimestampFor(string addressTo, int tokenId, bool decode = true)
        {
            try
            {
                string[] types = { "uint256" };
                object[] values = { tokenId };

                string result = _blockchain.ReadContract(addressTo, "keyExpirationTimestampFor", _abi, values).Result;
                if (decode) result = ProcessExpirationResult(result);
                return result;
            }
            catch (Exception ex)
            {
                _project.log(ex.InnerException?.Message ?? ex.Message);
                throw;
            }
        }
        
        public string GetUserMetadataAuthenticated(int chainId, string lockAddress, string userAddress)
        {
            if (string.IsNullOrEmpty(_bearerToken))
                throw new Exception("Not authenticated. Call Login() first.");
        
            string url = $"{BASE_URL}/{chainId}/locks/{lockAddress}/users/{userAddress}";
        
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_bearerToken}");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            
                var response = client.GetAsync(url).Result;
                response.EnsureSuccessStatusCode();
            
                return response.Content.ReadAsStringAsync().Result;
            }
        }

        public string GetKeyMetadataAuthenticated(int chainId, string lockAddress, int tokenId)
        {
            if (string.IsNullOrEmpty(_bearerToken))
                throw new Exception("Not authenticated. Call Login() first.");
        
            string url = $"{BASE_URL}/{chainId}/locks/{lockAddress}/keys/{tokenId}";
        
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_bearerToken}");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            
                var response = client.GetAsync(url).Result;
                response.EnsureSuccessStatusCode();
                return response.Content.ReadAsStringAsync().Result;
            }
        }
        
       public void CollectSubscribersToDb(int chainId, string lockAddress, int maxTokenId, string privateKey, string tableName = "___z3nFarm")
        {
            // Login first
            Login(privateKey);
            
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            for (int tokenId = 1; tokenId <= maxTokenId; tokenId++)
            {
                _project.Var("acc0", tokenId);
                
                try
                {
                    string json = GetKeyMetadataAuthenticated(chainId, lockAddress, tokenId);
                    _logger.Send(json);
                    
                    var data = JObject.Parse(json);

                    string owner = data["owner"]?.ToString();
                    if (string.IsNullOrEmpty(owner))
                    {
                        _logger.Send($"Token {tokenId}: no owner, stopping");
                        break;
                    }

                    // Безопасное извлечение всех полей с fallback на пустую строку
                    long expiration = long.Parse(data["expiration"]?.ToString() ?? "0");
                    string github = data["userMetadata"]?["protected"]?["github"]?.ToString() ?? "";
                    string email = data["userMetadata"]?["protected"]?["email"]?.ToString() ?? "";
                    bool expired = expiration <= now;
                    
                    var d = new Dictionary<string, string>
                    {
                        { "token_id", tokenId.ToString() },
                        { "owner", owner },
                        { "expired", expired.ToString() },
                        { "expiration", expiration.ToString() },
                        { "github", github },
                        { "email", email }
                    };
                    
                    _project.DicToDb(d, log: true);
                    _logger.Send($"Token {tokenId}: saved (github={github}, expired={expired})");
                }
                catch (Exception ex)
                {
                    _logger.Send($"!W Token {tokenId}: {ex.Message}");
                    // Сохранить хотя бы token_id с ошибкой
                    try
                    {
                        var errorDic = new Dictionary<string, string>
                        {
                            { "token_id", tokenId.ToString() },
                            { "owner", "" },
                            { "expired", "" },
                            { "expiration", "0" },
                            { "github", "" },
                            { "email", "" }
                        };
                        _project.DicToDb(errorDic,tableName, log: false);
                    }
                    catch { }
                    
                    // Не останавливаться на ошибке, продолжить
                    continue;
                }
            }
            
            _logger.Send($"Collected metadata for {maxTokenId} tokens");
        }
        public string ownerOf(string addressTo, int tokenId, bool decode = true)
        {
            try
            {
                string[] types = { "uint256" };
                object[] values = { tokenId };
                string result = _blockchain.ReadContract(addressTo, "ownerOf", _abi, values).Result;
                if (decode) result = Decode(result, "ownerOf");

                return result;
            }
            catch (Exception ex)
            {
                _project.log(ex.InnerException?.Message ?? ex.Message);
                throw;
            }
        }
        public Dictionary<string, string> GetActiveEmails(int chainId, string lockAddress, int maxTokenId = 1000)
        {
            var emails = new Dictionary<string, string>();
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    
            for (int tokenId = 1; tokenId <= maxTokenId; tokenId++)
            {
                try
                {
                    //string json = GetKeyMetadata(chainId, lockAddress, tokenId);
                    string json = GetKeyMetadataAuthenticated(chainId, lockAddress, tokenId);
                    
                    var data = JObject.Parse(json);
            
                    string owner = data["owner"]?.ToString();
                    if (string.IsNullOrEmpty(owner))
                    {
                        _logger.Send($"Token {tokenId} has no owner, stopping");
                        break;
                    }
                    
                    _project.ToJson(json);
                    string email = data["userMetadata"]?["protected"]?["email"]?.ToString();
                    if (string.IsNullOrEmpty(email))
                    {
                        _logger.Send($"Token {tokenId}: no email");
                        continue;
                    }
            
                    string expirationStr = data["expiration"]?.ToString();
                    if (string.IsNullOrEmpty(expirationStr))
                    {
                        _logger.Send($"Token {tokenId}: no expiration");
                        continue;
                    }
            
                    long expiration = long.Parse(expirationStr);
                    if (expiration <= now)
                    {
                        _logger.Send($"Token {tokenId}: expired");
                        continue;
                    }
            
                    emails[owner.ToLower()] = email;
                    _logger.Send($"Token {tokenId}: {email} active");
                }
                catch (Exception ex)
                {
                    _logger.Send($"Token {tokenId}: {ex.Message}, stopping");
                    break;
                }
            }
    
            return emails;
        }
        public string GetEmail(int chainId, string lockAddress, int tokenId)
        {
            try
            {
                string json = GetKeyMetadata(chainId, lockAddress, tokenId);
                var data = JObject.Parse(json);

                string email = data["userMetadata"]?["protected"]?["email"]?.ToString();

                if (string.IsNullOrEmpty(email))
                {
                    _logger.Send($"No email found for token {tokenId}");
                    return null;
                }

                return email;
            }
            catch (Exception ex)
            {
                _logger.Send($"!W Failed to get email: {ex.Message}");
                return null;
            }
        }
        public Dictionary<string, string> GetAllEmails(int chainId, string lockAddress, int maxTokenId = 1000)
        {
            var emails = new Dictionary<string, string>();
            
            for (int tokenId = 1; tokenId <= maxTokenId; tokenId++)
            {
                try
                {
                    string json = GetKeyMetadata(chainId, lockAddress, tokenId);
                    var data = JObject.Parse(json);
                    
                    string email = data["userMetadata"]?["protected"]?["email"]?.ToString();
                    if (string.IsNullOrEmpty(email)) continue;
                    
                    string owner = data["owner"]?.ToString();
                    if (string.IsNullOrEmpty(owner)) continue;
                    
                    emails[owner.ToLower()] = email;
                    _logger.Send($"Token {tokenId}: {owner} -> {email}");
                }
                catch
                {
                    break;
                }
            }
            
            return emails;
        }
        public string GetOwner(int chainId, string lockAddress, int tokenId)
        {
            try
            {
                string json = GetKeyMetadata(chainId, lockAddress, tokenId);
                var data = JObject.Parse(json);
                return data["owner"]?.ToString();
            }
            catch (Exception ex)
            {
                _logger.Send($"!W Failed to get owner: {ex.Message}");
                return null;
            }
        }

        public string GetName(int chainId, string lockAddress, int tokenId)
        {
            try
            {
                string json = GetKeyMetadata(chainId, lockAddress, tokenId);
                var data = JObject.Parse(json);
                return data["name"]?.ToString();
            }
            catch (Exception ex)
            {
                _logger.Send($"!W Failed to get name: {ex.Message}");
                return null;
            }
        }

        public string Decode(string toDecode, string function)
        {
            if (string.IsNullOrEmpty(toDecode))
            {
                _project.log("Result is empty, nothing to decode");
                return string.Empty;
            }

            if (toDecode.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) toDecode = toDecode.Substring(2);
            if (toDecode.Length < 64) toDecode = toDecode.PadLeft(64, '0');


            var decodedDataExpire = z3nCore.Decoder.AbiDataDecode(_abi, function, "0x" + toDecode);
            string decodedResultExpire = decodedDataExpire.Count == 1
                ? decodedDataExpire.First().Value
                : string.Join("\n", decodedDataExpire.Select(item => $"{item.Key};{item.Value}"));

            return decodedResultExpire;
        }

        string ProcessExpirationResult(string resultExpire)
        {
            if (string.IsNullOrEmpty(resultExpire))
            {
                _project.SendToLog("Result is empty, nothing to decode", LogType.Warning, true, LogColor.Yellow);
                return string.Empty;
            }

            if (resultExpire.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                resultExpire = resultExpire.Substring(2);
            }

            if (resultExpire.Length < 64)
            {
                resultExpire = resultExpire.PadLeft(64, '0');
            }

            var decodedDataExpire =
                z3nCore.Decoder.AbiDataDecode(_abi, "keyExpirationTimestampFor", "0x" + resultExpire);
            string decodedResultExpire = decodedDataExpire.Count == 1
                ? decodedDataExpire.First().Value
                : string.Join("\n", decodedDataExpire.Select(item => $"{item.Key};{item.Value}"));

            return decodedResultExpire;
        }

        public Dictionary<string, string> Holders(string contract)
        {
            var result = new Dictionary<string, string>();
            int i = 0;
            while (true)
            {
                i++;
                var owner = ownerOf(contract, i);
                if (owner == "0x0000000000000000000000000000000000000000") break;
                var exp = keyExpirationTimestampFor(contract, i);
                result.Add(owner.ToLower(), exp.ToLower());
            }

            return result;
        }
    
        
        
        #region Authentication
            
        public string Login(string privateKey)
        {
            try
            {
                // 1. Get nonce
                string nonce = GetNonce();
                _logger.Send($"Got nonce: {nonce}");
                
                // 2. Create SIWE message
                var account = new Account(privateKey);
                string address = account.Address;
                string siweMessage = CreateSiweMessage(address, nonce);
                
                // 3. Sign message
                var signer = new EthereumMessageSigner();
                string signature = signer.EncodeUTF8AndSign(siweMessage, new EthECKey(privateKey));
                
                _logger.Send($"Signed message for {address}");
                
                // 4. Login and get token
                string token = PerformLogin(siweMessage, signature);
                _bearerToken = token;
                
                _logger.Send($"✅ Authenticated successfully");
                return token;
            }
            catch (Exception ex)
            {
                _logger.Send($"!W Login failed: {ex.Message}");
                throw;
            }
        }
        
        private string GetNonce()
        {
            using (var client = new HttpClient())
            {
                var response = client.GetAsync($"{AUTH_BASE}/nonce").Result;
                response.EnsureSuccessStatusCode();
                return response.Content.ReadAsStringAsync().Result.Trim();
            }
        }
        
        private string CreateSiweMessage(string address, string nonce)
        {
            string issuedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    
            return "app.unlock-protocol.com wants you to sign in with your Ethereum account:\n" +
                   $"{address}\n\n" +
                   "By signing, you are proving you own this wallet and logging in. This does not initiate a transaction or cost any fees.\n\n" +
                   "URI: https://app.unlock-protocol.com\n" +
                   "Version: 1\n" +
                   "Chain ID: 8453\n" +
                   $"Nonce: {nonce}\n" +
                   $"Issued At: {issuedAt}\n" +
                   "Resources:\n" +
                   "- https://privy.io";
        }
        
        private string PerformLogin(string message, string signature)
        {
            var payload = new
            {
                message = message,
                signature = signature
            };
			
            var body = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
			_logger.Send(body);
            var jsonResponse = _project.POST($"{AUTH_BASE}/login", body);
			_logger.Send(jsonResponse);
            var data = JObject.Parse(jsonResponse);
                
            string accessToken = data["accessToken"]?.ToString();
            if (string.IsNullOrEmpty(accessToken))
                throw new Exception("No accessToken in response");
                    
            return accessToken;
            
        }
    
        #endregion
        
        
        
        #region Sync
        public Dictionary<string, string> GetActiveGitHubFromDb()
        {
            var users = new Dictionary<string, string>(); 
            var _db = new Db(_project);
            try
            {
                var activeRecords = _db.GetLines(
                    "owner, github", 
                    tableName: "___z3nFarm", 
                    where: "expired = 'False' AND github != ''",
                    log: true
                );
        
                foreach (var record in activeRecords)
                {
                    var parts = record.Split('¦');
                    if (parts.Length < 2) continue;
            
                    string owner = parts[0].Trim().ToLower();
                    string github = parts[1].Trim();
            
                    if (!string.IsNullOrEmpty(owner) && !string.IsNullOrEmpty(github))
                    {
                        users[owner] = github;
                        _logger.Send($"Active: {owner} → {github}");
                    }
                }
        
                _logger.Send($"Found {users.Count} active subscribers with GitHub");
            }
            catch (Exception ex)
            {
                _logger.Send($"!W Failed to get active users from DB: {ex.Message}");
            }
    
            return users;
        }
        #endregion
        
    }
}