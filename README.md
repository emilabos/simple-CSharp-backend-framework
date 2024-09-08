<link href="https://cdnjs.cloudflare.com/ajax/libs/prism/1.28.0/themes/prism.min.css" rel="stylesheet" />

# LimeBackend
A simple backend framework built in C# using HttpListener for handling HTTP requests. It allows you to create routes for handling different HTTP methods (GET, POST, etc.) and supports session management through cookies and authentication. Middleware can be added to handle tasks like logging, and the framework includes helper functions for rendering HTML, CSS, JavaScript, and JSON responses.

## Key Features
  <b>📡 Routing</b>: Easily define routes with support for GET and POST requests.<br />
  <b>⚙️ Middleware</b>: Add middleware for custom processing, such as logging or request manipulation.<br />
  <b>🗂 Session Management</b>: Built-in session handling and token generation for user authentication.<br />
  <b>🖥 Rendering Helpers</b>: Functions to parse request bodies and render HTML, CSS, JS, and JSON.<br />
  <b>🍪 Cookie Support</b>: Load and manage cookies for session tracking and authentication<br />

## Getting Started

### Routing
Routing is extremely easy with LimeBackend, simply creating an instance of the HttpServer and then using the Route() method to add routes.</br>
The Route() method takes the following parameters:</br>
  • method (GET, POST etc..) [string] </br>
  • path ( e.g. /dashboard or /home etc..) [string] </br>
  • auth (true if the route requires authentication) [bool] </br>
  • logic (the logic for the route) [Func<HttpListenerRequest, HttpListenerResponse, object?>] </br>

Example:
<pre><code class="language-csharp">
  HttpServer server = new();
  
  server.Route(method: "GET", path: "/", auth: false, logic: (request, response) =>
  {
      return HttpServer.RenderHTML(html_path: "html/index.html", css: ["css/style.css"], javacript: ["js/home.js"]);
  });
</code>
</pre>
