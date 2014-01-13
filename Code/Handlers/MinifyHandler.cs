namespace NTTData.SitecoreCDN.Handlers
{
    using System;
    using System.Text.RegularExpressions;
    using System.Web;
    using NTTData.SitecoreCDN.Configuration;
    using NTTData.SitecoreCDN.Minifiers;
    using NTTData.SitecoreCDN.Providers;
    using Sitecore;
    using Sitecore.Diagnostics;
    using Sitecore.IO;
    using Sitecore.SecurityModel.Cryptography;
    using Sitecore.Text;
    using Sitecore.Web;

    /// <summary>
    /// This handler will minify .js and .css file requests.
    /// </summary>
    public class MinifyHandler : IHttpHandler
    {
        private bool _success = false;

        public bool IsReusable
        {
            get { return true; }
        }

        public virtual bool Success
        {
            get { return this._success; }
        }

        public void ProcessRequest(HttpContext context)
        {
            // set from an earlier in pipeline by CDNInterceptPipeline 
            string minifyPath = StringUtil.GetString(context.Items["MinifyPath"]);

            if (string.IsNullOrEmpty(minifyPath))
            {
                context.Response.StatusCode = 404;
                context.Response.End();
            }
            
            var url = new UrlString(minifyPath);
            string localPath = url.Path;

            string filePath = FileUtil.MapPath(localPath);

            // if the request is a .js file
            if (localPath.EndsWith(".js"))
            {
                var hasher = new HashEncryption(HashEncryption.EncryptionProvider.MD5);
                if (!string.IsNullOrEmpty(localPath))
                {
                    // generate a unique filename for the cached .js version
                    string cachedFilePath = FileUtil.MapPath(string.Format("/App_Data/MediaCache/{0}.js", hasher.Hash(url.ToString())));

                    // if it doesn't exist create it
                    if (!FileUtil.FileExists(cachedFilePath))
                    {
                        // if the original file exsits minify it
                        if (FileUtil.FileExists(filePath))
                        {
                            var minifier = new JsMinifier();
                            string minified = minifier.Minify(filePath);
                            FileUtil.WriteToFile(cachedFilePath, minified);
                        }
                    }

                    if (FileUtil.FileExists(cachedFilePath))
                    {
                        context.Response.ClearHeaders();
                        context.Response.Cache.SetExpires(DateTime.Now.AddDays(14));
                        context.Response.AddHeader("Content-Type", "application/x-javascript; charset=utf-8");
                        context.Response.WriteFile(cachedFilePath);
                        context.Response.End();
                        this._success = true;
                    }
                }
            }
            // if the request is a .css file
            else if (localPath.EndsWith(".css"))
            {
                var hasher = new HashEncryption(HashEncryption.EncryptionProvider.MD5);
                if (!string.IsNullOrEmpty(localPath))
                {
                    // generate a unique filename for the cached .css version
                    string cachedFilePath = FileUtil.MapPath(string.Format("/App_Data/MediaCache/{0}.css", hasher.Hash(url.ToString())));

                    // if it doesn't exist create it
                    if (!FileUtil.FileExists(cachedFilePath))
                    {
                        // if the original file exsits minify it
                        if (FileUtil.FileExists(filePath))
                        {
                            CssMinifier minifier = new CssMinifier();
                            string minified = CssMinifier.Minify(FileUtil.ReadFromFile(filePath));

                            // if Css Processing is enabled, replace any urls inside the css file.
                            if (CDNSettings.ProcessCss)
                            {
                                // find all css occurences of url([url])
                                var regReplaceUrl = new Regex(CDNProvider.CssUrlRegexPattern);

                                try
                                {
                                    // replacing  url([url]) with url([cdnUrl]) in css
                                    minified = regReplaceUrl.Replace(
                                        minified,
                                        (m) =>
                                            {
                                                string oldUrl = string.Empty;
                                                if (m.Groups.Count > 1)
                                                {
                                                    oldUrl = m.Groups[1].Value;
                                                }

                                                if (WebUtil.IsInternalUrl(oldUrl))
                                                {
                                                    if (oldUrl.StartsWith("."))
                                                    {
                                                        oldUrl = VirtualPathUtility.Combine(url.Path, oldUrl);
                                                    }

                                                    string newUrl = CDNManager.ReplaceMediaUrl(oldUrl, string.Empty);
                                                    if (!string.IsNullOrEmpty(newUrl))
                                                    {
                                                        return m.Value.Replace(m.Groups[1].Value, newUrl);
                                                    }
                                                }

                                                return m.Value;
                                            });
                                }
                                catch (Exception ex)
                                {
                                    Log.Error("Minify error", ex, this);
                                }
                            }

                            FileUtil.WriteToFile(cachedFilePath, minified);
                        }
                    }

                    if (FileUtil.FileExists(cachedFilePath))
                    {
                        context.Response.ClearHeaders();
                        context.Response.Cache.SetExpires(DateTime.Now.AddDays(14));
                        context.Response.AddHeader("Content-Type", "text/css; charset=utf-8");
                        context.Response.TransmitFile(cachedFilePath);
                        context.Response.End();
                        this._success = true;
                    }
                }
            }
        }
    }
}
