﻿using System;
using System.IO;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using SensHub.Plugins;
using SensHub.Server;
using Splat;

namespace SensHub.Server.Http
{
    /// <summary>
    /// Implements a simple single threaded HTTP server to provide the UI.
    /// </summary>
    public class HttpServer : IEnableLogger
    {
        // Name of the cookie to use for sessions
        private const string SessionCookie = "SensHubSessionID";

        // The directory containing the site
        private string m_sitePath;

        // The actual listener
        private HttpListener m_listener;

        // URL handler instances
        private Dictionary<string, HttpRequestHandler> m_handlers;

        // Active sessions
        private Dictionary<Guid, HttpSession> m_sessions;

        // RPC call manager
        private RpcRequestHandler m_rpcHandler;

        public HttpServer(string sitePath)
        {
            m_sitePath = sitePath;
            m_sessions = new Dictionary<Guid, HttpSession>();
            m_handlers = new Dictionary<string, HttpRequestHandler>();
            AddHandler("/", new StaticFileHandler(m_sitePath));
            m_rpcHandler = new RpcRequestHandler();
            AddHandler("/api/", m_rpcHandler);
        }

        /// <summary>
        /// Attach a request handler to a given prefix.
        /// </summary>
        /// <param name="prefix"></param>
        /// <param name="handler"></param>
        public void AddHandler(string prefix, HttpRequestHandler handler)
        {
            m_handlers.Add(prefix, handler);
        }

        /// <summary>
        /// Convert a resource name (without the prefix) into a target
        /// path and file. Assumes that all files have a single suffix
        /// (eg .html, .css, etc) and all other dots in the name are
        /// replaced with a directory separator.
        /// </summary>
        /// <param name="resourceName"></param>
        /// <returns></returns>
        private string GetTargetFileName(string resourceName)
        {
            int lastDot = resourceName.LastIndexOf('.');
            return resourceName.Substring(0, lastDot).Replace('.', '/') + resourceName.Substring(lastDot);
        }

        /// <summary>
        /// Unpack the site into a static directory.
        /// 
        /// In a 'production' environment the server is fronted by an Nginx
        /// instance running as a caching proxy. To facilitate this we unpack
        /// the site from resources to a directory so it can access them.
        /// </summary>
        /// <param name="siteDir"></param>
        public void UnpackSite()
        {
            // Make sure we have an empty directory to start with
            Directory.Delete(m_sitePath, true);
            Directory.CreateDirectory(m_sitePath);
            // Walk through all the resources
            var assembly = Assembly.GetExecutingAssembly();
            string prefix = assembly.GetName().Name + ".Resources.Site.";
            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                if (!resourceName.StartsWith(prefix))
                    continue;
                string fileName = GetTargetFileName(resourceName.Substring(prefix.Length));
                // Make sure the directory exists
                string directory = Path.Combine(m_sitePath, Path.GetDirectoryName(fileName));
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);
                // Copy the resource in
                using (Stream source = assembly.GetManifestResourceStream(resourceName))
                {
                    Stream target = File.Create(Path.Combine(m_sitePath, fileName));
                    source.CopyTo(target);
                    target.Close();
                }
            }
        }

        /// <summary>
        /// Get (or create) a session instance for the given request.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        private HttpSession GetRequestSession(HttpListenerRequest request)
        {
            HttpSession session = null;
            // See if the cookie has a session
            Cookie cookie = request.Cookies[SessionCookie];
            if (cookie != null)
            {
                try
                {
                    Guid sessionID = Guid.Parse(cookie.Value);
                    if (!m_sessions.TryGetValue(sessionID, out session))
                        session = null;
                }
                catch
                {
                    // Invalid format, just ignore it
                }
            }
            if (session == null)
            {
                session = new HttpSession();
                session.RemoteAddress = request.RemoteEndPoint.Address.ToString();
                m_sessions[session.UUID] = session;
            }
            // Do some verification
            if (session.RemoteAddress != request.RemoteEndPoint.Address.ToString())
            {
                this.Log().Error("Attempt to use session from incorrect address - was {0}, now {1}.",
                    session.RemoteAddress,
                    request.RemoteEndPoint.Address.ToString()
                    );
                return null;
            }
            return session;
        }

        public string ProcessRequest(HttpListenerRequest request, HttpListenerResponse response)
        {
            // Get the session
            HttpSession session = GetRequestSession(request);
            if (session == null)
            {
                response.StatusCode = 403;
                response.StatusDescription = "Request denied.";
                return null;
            }
            session.LastAccess = DateTime.Now;
            response.Cookies.Add(new Cookie(SessionCookie, session.UUID.ToString()));
            this.Log().Debug("{0} - {1}", request.Url, session.UUID);
            // Find a matching handler
            HttpRequestHandler handler = null;
            string fullURI = request.Url.AbsolutePath;
            int matchLength = 0;
            foreach (string candidate in m_handlers.Keys)
            {
                int size = candidate.Length;
                if (fullURI.StartsWith(candidate) && (size > matchLength))
                {
                    handler = m_handlers[candidate];
                    matchLength = size;
                }
            }
            // Invoke the handler if we have one
            string result = null;
            if (handler == null)
            {
                response.StatusCode = 404;
                response.StatusDescription = "Not found.";
                response.KeepAlive = false;
            }
            else
            {
                try
                {
                    result = handler.HandleRequest(session, fullURI.Substring(matchLength), request, response);
                }
                catch (Exception ex)
                {
                    this.Log().Error("Failed to process request - {0}", ex.ToString());
                    response.StatusCode = 500;
                    response.StatusDescription = ex.ToString();
                    response.KeepAlive = false;
                }
            }
            return result;
        }

        /// <summary>
        /// Start the HTTP server.
        /// 
        /// This method returns immediately, leaving the requests to be
        /// processed on the system threadpool.
        /// </summary>
        public void Start()
        {
            // Set up the listener
            m_listener = new HttpListener();
            Configuration serverConfig = Locator.Current.GetService<Configuration>();
            string prefix = "http://*:" + serverConfig["httpPort"].ToString() + "/";
            this.Log().Debug("Server listening on {0}", prefix);
            m_listener.Prefixes.Add(prefix);
            m_listener.Start();
            // Process incoming requests on a thread pool
            ThreadPool.QueueUserWorkItem((o) =>
            {
                try
                {
                    while (m_listener.IsListening)
                    {
                        ThreadPool.QueueUserWorkItem((c) =>
                        {
                            var ctx = c as HttpListenerContext;
                            try
                            {
                                // Set default content type
                                ctx.Response.ContentType = "text/plain";
                                // Process the request
                                string response = ProcessRequest(ctx.Request, ctx.Response);
                                if (response != null)
                                {
                                    byte[] buf = Encoding.UTF8.GetBytes(response);
                                    ctx.Response.ContentLength64 = buf.Length;
                                    ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                                }
                            }
                            catch { } // suppress any exceptions
                            finally
                            {
                                // always close the stream
                                ctx.Response.OutputStream.Close();
                            }
                        }, m_listener.GetContext());
                    }
                }
                catch { } // suppress any exceptions
            });
        }

        /// <summary>
        /// Stop the server.
        /// </summary>
        public void Stop()
        {
            if (m_listener != null)
                m_listener.Stop();
        }
    }
}