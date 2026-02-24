using System;
using System.Collections.Generic;
using ZennoLab.CommandCenter;
using System.Globalization;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using ZennoLab.InterfacesLibrary.ProjectModel;

using z3nCore;




namespace z3nFarm
{
    public class Upshot
    {
        #region Essentials
        private readonly IZennoPosterProjectModel _project;
        private readonly Instance _instance;
        private readonly Logger _log;
        
        public string NextUsdClaim { get; set; }
        public string NextAmoeClaim { get; set; }
        public string WalletAddress { get; set; }
        public bool? CanClaimUsd { get; set; }
        public bool? CanClaimAmoe { get; set; }
        
        public Upshot(IZennoPosterProjectModel project, Instance instance, Logger logger = null)
        {
            _project= project;
            _instance= instance;
            _log = logger;
        }
    #endregion
        
    #region Auth

    public void CheckHeaders()
    {
        if (_project.Var("headers") == "")
            _project.SaveRequestHeadersToVariable(_instance,"api-prod.upshotcards.net/api/v1/users");
    }


    #endregion
    
    #region Api
    
    const string apiHost = "https://api-prod.upshotcards.net/api/v1/";
    
    public string CashState()
    {
        CheckHeaders();
        var response =  _project.GET(apiHost + "wallet/claim/daily-cash/status", "+", parse:true);
        var json = JObject.Parse(response);
        NextUsdClaim = DateTime.Parse(json["data"]["nextClaimAt"].ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind)
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        CanClaimUsd = json["data"]["claimReady"].ToObject<bool>();
        return response;
    }
    
    public string GetUSDT()
    {
        if (CanClaimUsd == null) CashState();
        
        if (CanClaimUsd == true)
        {
            CheckHeaders();
            var response = _project.POST(apiHost + "wallet/claim/daily-cash", "{}" ,"+", parse:true);
            var json = JObject.Parse(response);
            NextUsdClaim = DateTime.Parse(json["data"]["nextClaimAt"].ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind)
                .ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        }
        return NextUsdClaim;
    }
    public string AmoeState()
    {
        CheckHeaders();
        var response = _project.GET(apiHost + "amoe/status", "+", parse:true);
        Thread.Sleep(1000);
        _log.Send($"AmoeState response: {response}");
        var json = JObject.Parse(response);
        var nextClaim = json["data"]["nextClaimAt"]?.ToString() ?? "" ;
        NextAmoeClaim = (!string.IsNullOrEmpty(nextClaim)) ? DateTime.Parse(json["data"]["nextClaimAt"].ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind)
            .ToString("yyyy-MM-ddTHH:mm:ss.fffZ") : "";
        CanClaimAmoe = json["data"]["canClaim"].ToObject<bool>();
        _log.Send($"canCalim: {CanClaimAmoe}");
        return response;
    }
    
    public string ClaimAmoe()
    {
        if (CanClaimAmoe == null) 
            AmoeState();
        
        if (CanClaimAmoe == true)
        {
            var response = _project.POST(apiHost + "amoe/claim", "{}" ,"+", parse:true, useNetHttp:false);
            _log.Send(response);
            AmoeState();
        }
        return NextAmoeClaim;
    }
    public string Me()
    {
        CheckHeaders();
        var response =  _project.GET(apiHost + "users/me", "+", parse:true);
        var data = JObject.Parse(response)["data"];
        CanClaimAmoe = data["freeClaimActive"].ToObject<bool>();
        var toDb = new Dictionary<string, string>();
        string _id = data["_id"]?.ToString() ?? "";
        string walletAddress = data["walletAddress"]?.ToString() ?? "";
        WalletAddress = walletAddress;
        string referralCode = data["referralCode"]?.ToString() ?? "";
        string twitterVerifiedAt = data["twitterVerifiedAt"]?.ToString() ?? "notBinded";
        toDb.Add("_id",_id);
        toDb.Add("walletAddress",walletAddress);
        toDb.Add("referralcode",referralCode);
        toDb.Add("twitterVerifiedAt",twitterVerifiedAt);
        _project.DicToDb(toDb);
        return response;
    }
    public Dictionary<string, long> Wallet()
    {
        CheckHeaders();
        var response =  _project.GET(apiHost + "wallet", "+", parse:true);
        var data = JObject.Parse(response)["data"];
        
        long CASH = data["CASH"].ToObject<long>();
        long SHOT = data["SHOT"].ToObject<long>();
        long GOLD = data["GOLD"].ToObject<long>();
        
        return new Dictionary<string, long>
        {
            {"CASH",CASH},
            {"SHOT",SHOT},
            {"GOLD",GOLD},
        };
    }
    public string BuyPack(string packId)
    {
        CheckHeaders();
        var body = $"{{\"packId\":\"{packId}\",\"quantity\":1}}";
        return _project.POST(apiHost + "packs/buy", body ,"+", parse:true);
    }
    public string OpenPack(string packId)
    {
        CheckHeaders();
        var body = $"{{\"packId\":\"{packId}\",\"quantity\":1}}";
        return _project.POST(apiHost + "packs/open", body ,"+", parse:true);
    }
    public string Packs()
    {
        return _project.GET(apiHost + "packs?status%5Bne%5D=ARCHIVED&sortBy=id&page=1&search=&sortOrder=desc&perPage=60", "+", parse:true);
    }
    
    public string PackToBuy(string order =  "minLeft")
    {
        var packs = Packs();
        string packId = null;
        var json = JObject.Parse(packs);

        var packsDic = json["data"]
            .Where(p => p["status"].ToString() == "ACTIVE" && (int)p["remainingStock"] > 0)
            .ToDictionary(
                p => p["id"].ToString(), 
                p => (int)p["remainingStock"]
            );
        
        if (order == "minLeft")
        {
            string minId = packsDic.OrderBy(kvp => kvp.Value)
                .FirstOrDefault()
                .Key;
            packId = minId;
        }
        else if (order == "random")
        {
            var keys = packsDic.Keys.ToList();
            packId = keys[new Random().Next(keys.Count)];
        }
        
        return packId;
    }

    public string UserCards()
    {
        if (WalletAddress == null) Me();
        var response =  _project.GET(apiHost + $"cards/balances/{WalletAddress}", "+", parse:true);
        var data = JObject.Parse(response)["data"];
        return response;
    }
    public int EligibleForContest(string contestId)
    {
        var response =_project.GET(apiHost + $"cards/eligible-for-contest/{contestId}", "+", parse:true);
        var meta = JObject.Parse(response)["meta"];
        return int.Parse(meta["total"].ToString());
        
    }
    
    public Dictionary<string, int> Contests()
    {
        var response = _project.GET(apiHost + $"contests?status=LIVE", "+", parse:true);
        var data = JObject.Parse(response)["data"];

        return data
            .Where(p => DateTime.Parse(p["entryClosesAt"].ToString()) > DateTime.UtcNow)
            .ToDictionary(
                p => p["id"].ToString(),
                p => p["lineupSize"].ToObject<int>()
            );
    }

    public List<string> CheckIfAnyContestAvaliable()
    {
        var result = new List<string>();
        var contests = Contests();
        foreach (var pair in contests)
        {
            var contest = pair.Key;
            var cards = EligibleForContest(contest);
            if (cards >= pair.Value)
            {
                result.Add(contest);
                _log.Send($"eligible for contest {contest}");
            }
            else
            {
                _log.Send($"to less cards [{cards}] {contest}");
            }
        }
        return result;
    }



    #endregion
    }
}
