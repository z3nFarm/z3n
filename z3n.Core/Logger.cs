using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using ZennoLab.InterfacesLibrary.Enums.Log;
using ZennoLab.InterfacesLibrary.ProjectModel;
using System.Text;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using z3nCore;

namespace z3nCore
{
    public enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Off = 99
    }
    
    public class Logger
    {
        private readonly bool _fAcc, _fPort, _fTime, _fMem, _fCaller, _fWrap, _fForce;
        private readonly IZennoPosterProjectModel _project;
        private bool _logShow = false;
        private string _emoji = null;
        private readonly bool _persistent;
        private readonly Stopwatch _stopwatch;
        private int _timezone;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        private string _logHost;
        private readonly bool _http;

        public Logger(IZennoPosterProjectModel project, bool log = false, string classEmoji = null, bool persistent = true, LogLevel logLevel = LogLevel.Info, string logHost = null, bool http = true, int timezoneOffset = -5)
        {
            _project = project;
            _logShow = log || _project.Var("debug") == "True";
            _emoji = classEmoji;
            _persistent = persistent;
            _stopwatch = persistent ? Stopwatch.StartNew() : null;
            _http = http;
            _logHost = !string.IsNullOrEmpty(_project.GVar("logHost")) ? _project.GVar("logHost") : "http://localhost:10993/log";
            _timezone = timezoneOffset;
            
            
            string cfg = _project.Var("cfgLog") ?? "";
            _fAcc = cfg.Contains("acc");
            _fPort = cfg.Contains("port");
            _fTime = cfg.Contains("time");
            _fMem = cfg.Contains("memory");
            _fCaller = cfg.Contains("caller");
            _fWrap = cfg.Contains("wrap");
            _fForce = cfg.Contains("force");
        }
        public Logger( bool log = false, string classEmoji = null, bool persistent = true, LogLevel logLevel = LogLevel.Info, string logHost = null, bool http = true, int timezoneOffset = -5)
        {
            _project = null;
            _logShow = log ;
            _emoji = classEmoji;
            _persistent = persistent;
            _stopwatch = persistent ? Stopwatch.StartNew() : null;
            _http = http;
            _logHost =  "http://localhost:10993/log";
            _timezone = timezoneOffset;
            
            
            //string cfg = _project.Var("cfgLog") ?? "";
            _fAcc = false;//cfg.Contains("acc");
            _fPort = false;//cfg.Contains("port");
            _fTime = false;//cfg.Contains("time");
            _fMem = false;//cfg.Contains("memory");
            _fCaller = true;//cfg.Contains("caller");
            _fWrap = true;//cfg.Contains("wrap");
            _fForce = false;//cfg.Contains("force");
        }


        public void Send(object toLog,
            [CallerMemberName] string callerName = "",
            bool show = false, bool thrw = false, bool toZp = true,
            int cut = 0, bool wrap = true,
            LogType type = LogType.Info, LogColor color = LogColor.Default)
        {
     
            if (_fForce) { show = true; toZp = true; }
            
            if (!show && !_logShow) return;
            
            string header = string.Empty;
            string body = toLog?.ToString() ?? "null";

            if (_fWrap)
            {
                header = LogHeader(callerName); 
                if (cut > 0 && body.Count(c => c == '\n') > cut)
                    body = body.Replace("\r\n", " ").Replace('\n', ' ');
            
                body = $"\n          {(!string.IsNullOrEmpty(_emoji) ? $"[ {_emoji} ] " : "")}{body.Trim()}";
            }
            
            
            string toSend = header + body;
            if (toSend.Contains("!W")) type = LogType.Warning;
            if (toSend.Contains("!E")) type = LogType.Error;

            if (_project != null)
            {
                Execute(toSend, type, color, toZp, thrw);
            }
            
            if (_http && _project != null)
            {
                string prjName =  _project != null ? _project.Name.Replace(".zp", "") : "";
                string acc = _project != null ?  _project.Var("acc0") : "";
                string port = _project != null ? _project.Var("port"): "";
                string pid = _project != null ? _project.Var("pid") : "";
                string sessionId =  _project != null ?  _project.Var("varSessionId") : "";
                SendToHttpLogger(body, type, callerName, prjName, acc, port, pid ,sessionId);
            }
        }
        
        private void SendToHttpLogger(string message, LogType type, string caller, string prj, string acc, string port,  string pid, string session)
        { _ = Task.Run(async () =>
            {
                try
                {
                    var logData = new
                    {
                        machine = Environment.MachineName,
                        project = prj,
                        timestamp = DateTime.UtcNow.AddHours(_timezone).ToString("yyyy-MM-dd HH:mm:ss"),
                        level = type.ToString().ToUpper(),
                        account = acc,
                        session = session,
                        port = port,
                        pid = pid,
                        caller = caller,
                        extra = new { caller },
                        message = message.Trim(),
                    };

                    string json = JsonConvert.SerializeObject(logData);
            
                    using (var cts = new System.Threading.CancellationTokenSource(1000))
                    using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                    {
                        await _httpClient.PostAsync(_logHost, content, cts.Token);
                    }
                }
                catch { }
            });
        }      
        public void Warn(object toLog, [CallerMemberName] string callerName = "", bool show = false, bool thrw = false, bool toZp = true, int cut = 0, bool wrap = true, LogColor color = LogColor.Default)
        {
            Send(toLog, callerName, show, thrw, toZp, cut, wrap, type: LogType.Warning, color: color);
        }
        private string LogHeader(string callerName)
        {
            var sb = new StringBuilder();
            if (_project != null)
            {
                if (_fAcc) sb.Append($"  🤖 [{_project.Var("acc0")}]");
                if (_fTime) sb.Append($"  ⏱️ [{_project.Age<string>()}]");
                if (_fPort) sb.Append($"  🔌 [{_project.Var("instancePort")}]");
            }
            if (_fCaller) sb.Append($"  🔲 [{callerName}]");
            return sb.ToString();
        }
        private string LogBody(string toLog, int cut)
        {
            if (string.IsNullOrEmpty(toLog)) return string.Empty;
            
            if (cut > 0)
            {
                int lineCount = toLog.Count(c => c == '\n') + 1;
                if (lineCount > cut)
                {
                    toLog = toLog.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
                }
            }
            
            if (!string.IsNullOrEmpty(_emoji))
            {
                toLog = $"[ {_emoji} ] {toLog}";
            }
            return $"\n          {toLog.Trim()}";
        }
        private void Execute(string toSend, LogType type, LogColor color, bool toZp, bool thrw)
        {
            _project.SendToLog(toSend, type, toZp, color);
            if (thrw) throw new Exception($"{toSend}");
        }
    }
}

public static partial class ProjectExtensions
{
    public static void log(this IZennoPosterProjectModel project, object toLog, [CallerMemberName] string callerName = "", bool show = true, bool thrw = false, bool toZp = true)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(callerName, @"^M[a-f0-9]{32}$")) callerName = project.Name;
        new Logger(project, persistent: false).Send(toLog, callerName, show: show, thrw: thrw, toZp: toZp);
    }
    
    public static void warn(this IZennoPosterProjectModel project, string toLog, bool thrw = false, [CallerMemberName] string callerName = "", bool show = true, bool toZp = true)
    {
        new Logger(project).Warn(toLog, callerName, show: show, thrw: thrw, toZp: toZp);
    }
    
    public static void warn(this IZennoPosterProjectModel project, Exception ex, bool thrw = false, [CallerMemberName] string callerName = "", bool show = true, bool toZp = true)
    {
        new Logger(project).Warn(ex.Message, callerName, show: show, thrw: thrw, toZp: toZp);
    }
    
    internal static void ObsoleteCode(this IZennoPosterProjectModel project, string newName = "unknown")
    {
        try
        {
            if (project == null) return;

            var sb = new StringBuilder();

            try
            {
                var trace = new StackTrace(1, true);
                string oldName = "";
                string callerName = "";
                
                for (int i = 0; i < trace.FrameCount; i++)
                {
                    var frame = trace.GetFrame(i);
                    var method = frame?.GetMethod();
                    if (method == null || method.DeclaringType == null) continue;

                    var typeName = method.DeclaringType.FullName;
                    if (string.IsNullOrEmpty(typeName)) continue;

                    if (typeName.StartsWith("System.") || typeName.StartsWith("ZennoLab.")) continue;

                    var methodName = $"{typeName}.{method.Name}";
                    
                    if (i == 0) 
                    {
                        oldName = methodName;
                    }
                    else
                    {
                        callerName = methodName;
                        break;
                    }
                }
                
                if (string.IsNullOrEmpty(callerName) || callerName == "z3nCore.Init.RunProject") 
                    callerName = Path.Combine(project.Path, project.Name);

                sb.Append($"![OBSOLETE CODE]. Obsolete method: [{oldName}] called from: [{callerName}]");
                if (!string.IsNullOrEmpty(newName)) sb.Append($". Use: [{newName}] instead");
                
                project.SendWarningToLog(sb.ToString().Trim(), true);
            }
            catch (Exception ex)
            {
                try
                {
                    project.SendToLog($"!E WarnObsolete logging failed: {ex.Message}", LogType.Error, true, LogColor.Red);
                }
                catch { }
            }
        }
        catch { }
    }
}