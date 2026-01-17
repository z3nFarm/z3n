using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using ZennoLab.CommandCenter;
using ZennoLab.InterfacesLibrary.ProjectModel;
using Newtonsoft.Json.Linq;

namespace z3nCore
{
    /// <summary>
    /// Работа с трафиком браузера - поиск и извлечение данных из HTTP запросов/ответов
    /// </summary>
    public partial class Traffic
    {
        #region Fields & Constructor

        private readonly IZennoPosterProjectModel _project;
        private readonly Instance _instance;
        private readonly Logger _logger;
        private readonly bool _showLog;

        // Внутренний кэш (скрыт от пользователя)
        private List<TrafficElement> _cache;
        private DateTime _cacheTime;
        private const int CACHE_LIFETIME_SECONDS = 2;

        public Traffic(IZennoPosterProjectModel project, Instance instance, bool log = false)
        {
            _project = project;
            _instance = instance;
            _showLog = log;
            _logger = new Logger(project, log: log, classEmoji: "🌎");
            _instance.UseTrafficMonitoring = true;
        }

        #endregion

        #region Find Traffic Elements (Поиск элементов трафика)

        /// <summary>
        /// Найти первый элемент трафика по URL (с ожиданием)
        /// </summary>
        /// <param name="url">URL или его часть для поиска</param>
        /// <param name="strict">true = точное совпадение, false = содержит подстроку</param>
        /// <param name="timeoutSeconds">Таймаут ожидания в секундах</param>
        /// <param name="retryDelaySeconds">Задержка между попытками</param>
        public TrafficElement FindTrafficElement(string url, bool strict = false, 
            int timeoutSeconds = 15, int retryDelaySeconds = 1, bool reload = false)
        {
            if (reload) ReloadPage();
            _project.Deadline();
            _instance.UseTrafficMonitoring = true;

            var startTime = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            int attemptNumber = 0;

            while (DateTime.Now - startTime < timeout)
            {
                _project.Deadline(timeoutSeconds);
                attemptNumber++;

                if (_showLog) _logger.Send($"Attempt #{attemptNumber} searching URL: {url}");

                var element = SearchInCache(url, strict);
                if (element != null)
                {
                    if (_showLog) _logger.Send($"✓ Found traffic for: {url}");
                    return element;
                }

                Thread.Sleep(1000 * retryDelaySeconds);
            }

            throw new TimeoutException(
                $"Traffic element not found for URL '{url}' within {timeoutSeconds} seconds");
        }

        /// <summary>
        /// Найти все элементы трафика по URL (без ожидания, работает с текущим кэшем)
        /// </summary>
        /// <param name="url">URL или его часть для поиска</param>
        /// <param name="strict">true = точное совпадение, false = содержит подстроку</param>
        public List<TrafficElement> FindAllTrafficElements(string url, bool strict = false)
        {
            UpdateCacheIfNeeded();

            var matches = new List<TrafficElement>();

            foreach (var element in _cache)
            {
                bool isMatch = strict 
                    ? element.Url == url 
                    : element.Url.Contains(url);

                if (isMatch)
                {
                    matches.Add(element);
                }
            }

            if (_showLog) _logger.Send($"Found {matches.Count} traffic elements for: {url}");

            return matches;
        }

        /// <summary>
        /// Получить весь текущий трафик (все элементы)
        /// </summary>
        public List<TrafficElement> GetAllTraffic()
        {
            UpdateCacheIfNeeded();
            return new List<TrafficElement>(_cache);
        }

        #endregion

        #region Get Specific Data (Получение конкретных данных - короткие пути)

        /// <summary>
        /// Получить тело ответа (response body) по URL
        /// </summary>
        public string GetResponseBody(string url, bool strict = false, int timeoutSeconds = 15)
        {
            var element = FindTrafficElement(url, strict, timeoutSeconds);
            return element.ResponseBody;
        }

        /// <summary>
        /// Получить тело запроса (request body) по URL
        /// </summary>
        public string GetRequestBody(string url, bool strict = false, int timeoutSeconds = 15)
        {
            var element = FindTrafficElement(url, strict, timeoutSeconds);
            return element.RequestBody;
        }

        /// <summary>
        /// Получить заголовок из запроса (request header)
        /// </summary>
        public string GetRequestHeader(string url, string headerName, bool strict = false, 
            int timeoutSeconds = 15)
        {
            var element = FindTrafficElement(url, strict, timeoutSeconds);
            return element.GetRequestHeader(headerName);
        }

        /// <summary>
        /// Получить заголовок из ответа (response header)
        /// </summary>
        public string GetResponseHeader(string url, string headerName, bool strict = false, 
            int timeoutSeconds = 15)
        {
            var element = FindTrafficElement(url, strict, timeoutSeconds);
            return element.GetResponseHeader(headerName);
        }

        /// <summary>
        /// Получить все заголовки запроса в виде словаря
        /// </summary>
        public Dictionary<string, string> GetAllRequestHeaders(string url, bool strict = false, 
            int timeoutSeconds = 15)
        {
            var element = FindTrafficElement(url, strict, timeoutSeconds);
            return element.GetAllRequestHeaders();
        }

        /// <summary>
        /// Получить все заголовки ответа в виде словаря
        /// </summary>
        public Dictionary<string, string> GetAllResponseHeaders(string url, bool strict = false, 
            int timeoutSeconds = 15)
        {
            var element = FindTrafficElement(url, strict, timeoutSeconds);
            return element.GetAllResponseHeaders();
        }
        
        public string GetApiStructure(string urlFilter)
        {
            var t = new Traffic(_project, _instance, true);
            var all = t.FindAllTrafficElements(urlFilter);
    
            var uniqueEndpoints = new Dictionary<string, JObject>(); // url -> first example
    
            foreach (var el in all)
            {
                string key = $"{el.Method}:{el.Url}"; // GET:url и POST:url - разные эндпоинты
        
                if (uniqueEndpoints.ContainsKey(key))
                    continue; // Уже есть пример этого эндпоинта
        
                var item = new JObject();
                item["method"] = el.Method;
                item["url"] = el.Url;
                item["statusCode"] = el.StatusCode;
        
                if (!string.IsNullOrEmpty(el.RequestBody))
                {
                    try { item["requestBody"] = JToken.Parse(el.RequestBody); }
                    catch { item["requestBody"] = el.RequestBody; }
                }
        
                if (!string.IsNullOrEmpty(el.ResponseBody))
                {
                    try { item["responseBody"] = JToken.Parse(el.ResponseBody); }
                    catch { item["responseBody"] = el.ResponseBody; }
                }
        
                uniqueEndpoints[key] = item;
            }
    
            var snapshot = new JObject();
            snapshot["totalEndpoints"] = uniqueEndpoints.Count;
            snapshot["endpoints"] = new JArray(uniqueEndpoints.Values);
    
            string json = snapshot.ToString(Newtonsoft.Json.  Formatting.Indented);
            
            return json;
        }

        #endregion

        #region Page Actions (Действия со страницей)

        /// <summary>
        /// Перезагрузить страницу и обновить кэш трафика
        /// </summary>
        public Traffic ReloadPage(int delaySeconds = 1)
        {
            _project.Deadline();

            _instance.ActiveTab.MainDocument.EvaluateScript("location.reload(true)");
            if (_instance.ActiveTab.IsBusy) _instance.ActiveTab.WaitDownloading();
            
            Thread.Sleep(1000 * delaySeconds);
            
            ForceRefreshCache();

            return this;
        }

        /// <summary>
        /// Явно обновить кэш трафика (обычно не требуется - обновляется автоматически)
        /// </summary>
        public Traffic RefreshTrafficCache()
        {
            ForceRefreshCache();
            return this;
        }

        #endregion

        #region Internal Cache Management (Внутреннее управление кэшем - скрыто от API)

        private void UpdateCacheIfNeeded()
        {
            bool cacheExpired = _cache == null || 
                                (DateTime.Now - _cacheTime).TotalSeconds > CACHE_LIFETIME_SECONDS;

            if (cacheExpired)
            {
                ForceRefreshCache();
            }
        }

        private void ForceRefreshCache()
        {
            var rawTraffic = _instance.ActiveTab.GetTraffic();
            _cache = new List<TrafficElement>();

            foreach (var item in rawTraffic)
            {
                // Пропускаем OPTIONS запросы
                if (item.Method == "OPTIONS") continue;

                _cache.Add(ConvertToTrafficElement(item));
            }

            _cacheTime = DateTime.Now;

            if (_showLog) _logger.Send($"Cache refreshed: {_cache.Count} elements");
        }

        private TrafficElement SearchInCache(string url, bool strict)
        {
            UpdateCacheIfNeeded();

            foreach (var element in _cache)
            {
                bool isMatch = strict 
                    ? element.Url == url 
                    : element.Url.Contains(url);

                if (isMatch)
                {
                    return element;
                }
            }

            // Не нашли - принудительно обновляем кэш и ищем еще раз
            ForceRefreshCache();

            foreach (var element in _cache)
            {
                bool isMatch = strict 
                    ? element.Url == url 
                    : element.Url.Contains(url);

                if (isMatch)
                {
                    return element;
                }
            }

            return null;
        }

        private TrafficElement ConvertToTrafficElement(dynamic rawItem)
        {
            var responseBody = rawItem.ResponseBody == null
                ? string.Empty
                : Encoding.UTF8.GetString(rawItem.ResponseBody, 0, rawItem.ResponseBody.Length);

            return new TrafficElement(_project)
            {
                Method = rawItem.Method ?? string.Empty,
                StatusCode = rawItem.ResultCode.ToString(),
                Url = rawItem.Url ?? string.Empty,
                ResponseContentType = rawItem.ResponseContentType ?? string.Empty,
                RequestHeaders = rawItem.RequestHeaders ?? string.Empty,
                RequestCookies = rawItem.RequestCookies ?? string.Empty,
                RequestBody = rawItem.RequestBody ?? string.Empty,
                ResponseHeaders = rawItem.ResponseHeaders ?? string.Empty,
                ResponseCookies = rawItem.ResponseCookies ?? string.Empty,
                ResponseBody = responseBody
            };
        }

        #endregion

        #region Nested Class - TrafficElement

        /// <summary>
        /// Один элемент трафика (HTTP запрос + ответ)
        /// </summary>
        public class TrafficElement
        {
            private readonly IZennoPosterProjectModel _project;

            internal TrafficElement(IZennoPosterProjectModel project)
            {
                _project = project;
            }

            // HTTP Request
            public string Method { get; internal set; }
            public string Url { get; internal set; }
            public string RequestHeaders { get; internal set; }
            public string RequestCookies { get; internal set; }
            public string RequestBody { get; internal set; }

            // HTTP Response
            public string StatusCode { get; internal set; }
            public string ResponseContentType { get; internal set; }
            public string ResponseHeaders { get; internal set; }
            public string ResponseCookies { get; internal set; }
            public string ResponseBody { get; internal set; }

            /// <summary>
            /// Распарсить ResponseBody как JSON в project.Json
            /// </summary>
            public TrafficElement ParseResponseBodyAsJson()
            {
                if (!string.IsNullOrEmpty(ResponseBody))
                {
                    _project.Json.FromString(ResponseBody);
                }
                return this;
            }

            /// <summary>
            /// Распарсить RequestBody как JSON в project.Json
            /// </summary>
            public TrafficElement ParseRequestBodyAsJson()
            {
                if (!string.IsNullOrEmpty(RequestBody))
                {
                    _project.Json.FromString(RequestBody);
                }
                return this;
            }

            /// <summary>
            /// Получить конкретный заголовок из запроса
            /// </summary>
            public string GetRequestHeader(string headerName)
            {
                var headers = ParseHeaders(RequestHeaders);
                var key = headerName.ToLower();
                return headers.ContainsKey(key) ? headers[key] : null;
            }

            /// <summary>
            /// Получить конкретный заголовок из ответа
            /// </summary>
            public string GetResponseHeader(string headerName)
            {
                var headers = ParseHeaders(ResponseHeaders);
                var key = headerName.ToLower();
                return headers.ContainsKey(key) ? headers[key] : null;
            }

            /// <summary>
            /// Получить все заголовки запроса в виде словаря
            /// </summary>
            public Dictionary<string, string> GetAllRequestHeaders()
            {
                return ParseHeaders(RequestHeaders);
            }

            /// <summary>
            /// Получить все заголовки ответа в виде словаря
            /// </summary>
            public Dictionary<string, string> GetAllResponseHeaders()
            {
                return ParseHeaders(ResponseHeaders);
            }

            private Dictionary<string, string> ParseHeaders(string headersString)
            {
                var headers = new Dictionary<string, string>();
                if (string.IsNullOrWhiteSpace(headersString)) return headers;

                foreach (var line in headersString.Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;

                    var colonIndex = trimmed.IndexOf(':');
                    if (colonIndex <= 0) continue;

                    var key = trimmed.Substring(0, colonIndex).Trim().ToLower();
                    var value = trimmed.Substring(colonIndex + 1).Trim();
                    headers[key] = value;
                }

                return headers;
            }
        }

        #endregion
    }


    #region Extension Methods

    public static partial class ProjectExtensions
    {
        /// <summary>
        /// Получить заголовки запроса и сохранить в переменную проекта
        /// </summary>
        public static void SaveRequestHeadersToVariable(this IZennoPosterProjectModel project, 
            Instance instance, string url, bool strict = false, bool log = false)
        {
            var traffic = new Traffic(project, instance, log: log);
            var element = traffic.FindTrafficElement(url, strict);
            
            var cleanHeaders = new StringBuilder();
            foreach (string header in element.RequestHeaders.Split('\n'))
            {
                // Пропускаем псевдо-заголовки HTTP/2
                if (header.StartsWith(":")) continue;
                if (string.IsNullOrWhiteSpace(header)) continue;
                
                cleanHeaders.AppendLine(header.Trim());
            }
            
            project.Var("headers", cleanHeaders.ToString());
            
            if (log) project.log($"Headers saved to variable 'headers':\n{cleanHeaders}");
        }

        /// <summary>
        /// Получить заголовки и сохранить в переменную проекта и/или БД
        /// </summary>
        public static void CollectRequestHeaders(this IZennoPosterProjectModel project, 
            Instance instance, string url, bool strict = false, 
            bool saveToVariable = true, bool saveToDatabase = true, bool log = false)
        {
            var traffic = new Traffic(project, instance, log: log);
            var element = traffic.FindTrafficElement(url, strict);
            
            var cleanHeaders = new StringBuilder();
            int headerCount = 0;
            
            foreach (string header in element.RequestHeaders.Split('\n'))
            {
                // Пропускаем псевдо-заголовки HTTP/2
                if (header.StartsWith(":")) continue;
                if (string.IsNullOrWhiteSpace(header)) continue;
                
                cleanHeaders.AppendLine(header.Trim());
                headerCount++;
            }
            
            var headersText = cleanHeaders.ToString();
            
            if (log) 
                project.log($"[SUCCESS]: collected={headerCount}, length={headersText.Length}\n{headersText}");
            
            if (saveToVariable) 
                project.Var("headers", headersText);
            
            if (saveToDatabase) 
                project.DbUpd($"headers = '{headersText}'");
        }
    }

    #endregion
}