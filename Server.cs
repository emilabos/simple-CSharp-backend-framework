using System.Text;
using System.Net;
using System.Text.Json;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace LimeBackend
{
    class Session
    {
        required public string Expiry { get; set; }
        required public string Token { get; set; }
        public bool LoggedIn { get; set; }
    }
    class Middleware
    {
        public static Tuple<HttpListenerRequest, HttpListenerResponse> Logging(HttpListenerRequest request, HttpListenerResponse response)
        {
            Console.WriteLine($"[{DateTime.Now}] {request.HttpMethod} request to {request.Url}");
            return new(request, response);
        }
    }

    class HttpServer
    {
        public static HttpListener? listener;
        public static readonly string[] validMethods = ["GET", "POST"];
        public List<Tuple<string, string, bool, Func<HttpListenerRequest?, HttpListenerResponse?, object?>>> routes = [];
        public int requestCount = 0;
        public List<Func<HttpListenerRequest, HttpListenerResponse, Tuple<HttpListenerRequest, HttpListenerResponse>>> middleware = [];
        public static string currentPageData = "";
        private static readonly char[] _allowedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()_".ToCharArray();
        private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

        public List<Dictionary<string, Session>> sessions = [];

        public static string GenerateToken(int length)
        {
            if (length <= 0)
            {
                throw new ArgumentException("Length must be greater than zero.", nameof(length));
            }

            var token = new char[length];
            var bytes = new byte[length * sizeof(char)];

            _rng.GetBytes(bytes);

            for (var i = 0; i < length; i++)
            {
                var index = (bytes[i] & 0x7F) % _allowedChars.Length;
                token[i] = _allowedChars[index];
            }

            return new string(token);
        }

        public static Dictionary <string, string> ParseRequestBodyToDict(HttpListenerRequest request)
        {
            Dictionary <string, string> output = [];
            using StreamReader reader = new(request!.InputStream, request.ContentEncoding);
            string requestBody = reader.ReadToEnd();
            
            if (requestBody.Contains('&'))
            {
                foreach (string component in requestBody.Split("&"))
                {
                    string[] componentSplit = component.Split("=");
                    output.Add(componentSplit[0], componentSplit[1]);
                }
            } else if (requestBody.Length > 0)
            {
                string[] componentSplit = requestBody.Split("=");
                output.Add(componentSplit[0], componentSplit[1]);
            }

            return output;
        }

        public static string ParseRequestBodyToString(HttpListenerRequest request)
        {
            using StreamReader reader = new(request!.InputStream, request.ContentEncoding);
            string requestBody = reader.ReadToEnd();

            return requestBody;
        }

        private static string RenderCSS(string path)
        {
            try
            {
                string cssData = File.ReadAllText(path);
                return $"<style>{cssData}</style>";
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine($"[FILE PATH ERROR] {path} path does not exist!");
                return "";
            }
        }

        private static string RenderJS(string path)
        {
            try
            {
                string jsData = File.ReadAllText(path);
                return $"<script>{jsData}</script>";
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine($"[FILE PATH ERROR] {path} path does not exist!");
                return "";
            }
        }

        public static string RenderHTML(string html_path, string[]? css, string[]? javascript)
        {
            try
            {
                string data = File.ReadAllText(html_path);

                string cssTag = "";
                string jsTag = "";

                foreach (var file in css!)
                {
                    cssTag += RenderCSS(file);
                }
                int cssInsertIndex = data.IndexOf("</head>");
                data = data.Insert(cssInsertIndex, cssTag);

                foreach (var file in javascript!)
                {
                    jsTag += RenderJS(file);
                }
                int jsInsertIndex = data.IndexOf("</body>");
                if (jsInsertIndex >= 0)
                {
                    data = data.Insert(jsInsertIndex, jsTag);
                }
                else
                {
                    data += jsTag;
                }

                currentPageData = data;
                return data;
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine($"[FILE PATH ERROR] {html_path} path does not exist!");
                return $"{html_path} was not found.";
            }
        }

        public static string RenderJSON(object json, bool prettyPrint)
        {
            try
            {
                var options = new JsonSerializerOptions()
                {
                    WriteIndented = prettyPrint
                };
                return JsonSerializer.Serialize(json, options);
            }
            catch (JsonException)
            {
                Console.WriteLine("[JSON ERROR] Invalid JSON passed as argument!");
                return string.Empty;
            }
        }
       
       public void Route(string method, string path, bool auth, Func<HttpListenerRequest?, HttpListenerResponse?, object?> logic)
        {
            if (validMethods.Contains(method))
            {
                if (path[0] == '/')
                {
                    var r = new Tuple<string, string, bool, Func<HttpListenerRequest?, HttpListenerResponse?, object?>>(method, path, auth, logic);
                    routes.Add(r);
                }
                else
                {
                    Console.WriteLine($"URL Path '{path}' is not valid!");
                }
            }
            else
            {
                Console.WriteLine($"Method '{method}' is invalid for {path}");
            }
        }
        
        private Dictionary<string, (string value, DateTime? expires, bool isSecure)> LoadCookies(HttpListenerRequest request)
        {
            Dictionary<string, (string value, DateTime? expires, bool isSecure)> cookiesDict = new();

            foreach (Cookie cookie in request.Cookies)
            {
                if (!cookie.Expired) 
                {
                    string name = cookie.Name;
                    string value = cookie.Value;
                    DateTime? expirationDate = cookie.Expires == DateTime.MinValue ? null : (DateTime?)cookie.Expires;
                    bool isSecure = cookie.Secure;

                    cookiesDict.Add(name, (value, expirationDate, isSecure));
                } else
                {
                    DestroySession(cookie.Name);
                }
            }

            return cookiesDict;
        }

        public async Task HandleIncomingRequests()
        {
            while (true)
            {
                HttpListenerContext context = await listener!.GetContextAsync();
                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;

                middleware.ForEach(func =>
                {
                    var result = func(request, response);
                    request = result.Item1;
                    response = result.Item2;
                });

                requestCount += 1;

                var matchedRoute = routes.FirstOrDefault(route => route.Item1 == request.HttpMethod && route.Item2 == request.Url!.AbsolutePath);

                if (matchedRoute != null)
                {
                    bool access;
                    if (!matchedRoute.Item3) // Route doesn't require authentication
                    {
                        access = true;
                    }
                    else
                    {
                        // Authorization check
                        var cookies = LoadCookies(request);
                        if (cookies.ContainsKey("auth") && sessions.Any(s => s.ContainsKey(cookies["auth"].value)))
                        {
                            access = true;
                        }
                        else
                        {
                            access = false;
                        }
                    }

                    if (access)
                    {
                        object? result = matchedRoute.Item4(request, response);
                        string responseData = result?.ToString() ?? string.Empty;
                        byte[] data = Encoding.UTF8.GetBytes(responseData);

                        if (responseData.TrimStart().StartsWith("{") || responseData.TrimStart().StartsWith("["))
                        {
                            response.ContentType = "application/json";
                        }
                        else
                        {
                            response.ContentType = "text/html";
                        }

                        response.ContentEncoding = Encoding.UTF8;
                        response.ContentLength64 = data.LongLength;
                        await response.OutputStream.WriteAsync(data);
                    }
                    else
                    {
                        // Unauthorized
                        Console.WriteLine($"[401] {request.Url!.AbsolutePath} access denied!");
                        string unauthorized = "<h1>401 - Unauthorized</h1>";
                        byte[] unauthorizedData = Encoding.UTF8.GetBytes(unauthorized);
                        response.StatusCode = 401;
                        response.ContentType = "text/html";
                        response.ContentEncoding = Encoding.UTF8;
                        response.ContentLength64 = unauthorizedData.LongLength;
                        await response.OutputStream.WriteAsync(unauthorizedData);
                    }
                }
                else
                {
                    // 404 Not Found
                    Console.WriteLine($"[404] {request.Url!.AbsolutePath} was not found!");
                    string notFound = "<h1>404 - Not Found</h1>";
                    byte[] notFoundData = Encoding.UTF8.GetBytes(notFound);
                    response.StatusCode = 404;
                    response.ContentType = "text/html";
                    response.ContentEncoding = Encoding.UTF8;
                    response.ContentLength64 = notFoundData.LongLength;
                    await response.OutputStream.WriteAsync(notFoundData);
                }

                response.Close();
            }
        }


        public void Start(int port)
        {
            string url = $"http://localhost:{port}/";
            listener = new HttpListener();
            listener.Prefixes.Add(url);
            listener.Start();
            if (listener.IsListening)
            {
                Console.WriteLine($"Running on http://localhost:{port}");
                Task listenTask = HandleIncomingRequests();
                listenTask.GetAwaiter().GetResult();
            }
            else
            {
                Console.WriteLine($"[500] HttpListener was unable to listen. Please try again.");
            }
            listener.Stop();
        }

        public void Use(Func<HttpListenerRequest, HttpListenerResponse, Tuple<HttpListenerRequest, HttpListenerResponse>> function)
        {
            middleware.Add(function);
        }

        public void CreateSession(string token)
        {
            if (sessions.Any(item => item.ContainsKey(token)))
            {
                Console.WriteLine($"Session with token {token[3..]}******* already exists");
            } else 
            {
                Session s = new()
                {
                    Expiry = DateTime.UtcNow.ToString(),
                    Token = token,
                    LoggedIn = true
                };
                sessions.Add(new  Dictionary<string, Session> { { token, s } });
                int sessionCount = sessions.Count;
                Console.WriteLine($"Created new session #{sessionCount}");
            }
        }

        public void DestroySession(string token)
        {
            if (sessions.Any(item => item.ContainsKey(token)))
            {
                int sessionCount = sessions.Count;
                var s = sessions.FirstOrDefault(item => item.ContainsKey(token));
                sessions.Remove(s!);
                Console.WriteLine($"Detroyed session #{sessionCount}");
            } else 
            {
                Console.WriteLine($"The session with token {token} does not exist!");
                
            }
        }
    }

    
    class Program
    {
        public static void Main(string[] args)
        {
            HttpServer server = new();

            server.Use(Middleware.Logging);

            server.Route("GET", "/", false, (request, response) =>
            {
                return HttpServer.RenderHTML("html/index.html", [], []);
            });

            server.Route("POST", "/auth", false, (request, response) =>
            {
                Dictionary<string, string> requestBody = HttpServer.ParseRequestBodyToDict(request!);

                string? username = requestBody.TryGetValue("username", out string? val_u) ? val_u : null;
                string? password = requestBody.TryGetValue("password", out string? val_p) ? val_p : null;

                // Generate random token
                string token = HttpServer.GenerateToken(32);
                server.CreateSession(token);

                // Set the cookie
                Cookie authCookie = new("auth", token)
                {
                    Expires = DateTime.UtcNow.AddHours(1), // Cookie expiry time
                    Secure = true, // Mark the cookie as secure
                    HttpOnly = true
                };
                response!.Cookies.Add(authCookie);

                response!.Redirect("/dashboard");
                return "";
            });

            server.Route("GET", "/dashboard", true, (request, response) =>
            {
                return "Welcome to your dashboard!";
            });

            server.Start(port: 8000);
        }
}
}
