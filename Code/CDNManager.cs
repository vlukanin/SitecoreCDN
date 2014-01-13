﻿namespace NTTData.SitecoreCDN
{
    using System.IO;
    using System.Text;
    using System.Web;
    using System.Web.UI;
    using HtmlAgilityPack;
    using NTTData.SitecoreCDN.Configuration;
    using NTTData.SitecoreCDN.Providers;
    using Sitecore;
    using Sitecore.Configuration;
    using Sitecore.Data.Items;
    using Sitecore.Sites;

    /// <summary>
    /// Static manager wrapper for CDNProvider
    /// </summary>
    public static class CDNManager
    {
        private static CDNProvider _provider;

        static CDNManager()
        {
            _provider = Factory.CreateObject("cdn/provider", true) as CDNProvider;
        }

        /// <summary>
        /// Cloud Front Provider
        /// </summary>
        public static CDNProvider Provider
        {
            get
            {
                return _provider;
            }
        }

        public static string StopToken
        {
            get
            {
                return _provider.StopToken;
            }
        }

        /// <summary>
        /// A variable to be set/get through the pipeline.  Will be true if {CDNManager.QueryStringToken} was found in the request
        /// </summary>
        public static bool IsCDNRequest
        {
            get
            {
                return MainUtil.GetBool(Sitecore.Context.Items["sc_IsCdnRequest"], false);
            }

            set
            {
                Sitecore.Context.Items["sc_IsCdnRequest"] = value;
            }
        }

        /// <summary>
        /// replace appropriate media urls in a full HtmlDocument
        /// </summary>
        /// <param name="doc"></param>
        public static void ReplaceMediaUrls(HtmlDocument doc)
        {
            _provider.ReplaceMediaUrls(doc);
        }


        /// <summary>
        /// Rewrites media urls to point to CDN hostname and dehydrates querystring into filename
        /// </summary>
        /// <param name="inputUrl">/path/to/file.ext?a=1&b=2</param>
        /// <returns>http://cdnHostname/path/to/file!cf!a=1!b=2.ext</returns>
        public static string ReplaceMediaUrl(string inputUrl, string cdnHostname)
        {
            return _provider.ReplaceMediaUrl(inputUrl, cdnHostname);
        }

        /// <summary>
        /// Tells you if the url is excluded by ExcludeUrlPatterns in .config
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public static bool UrlIsExluded(string url)
        {
            return _provider.UrlIsExluded(url);
        }

        /// <summary>
        /// Tells you if an incoming request's url should have it's contents Url replaced.
        /// ProcessRequestPatterns in .config
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public static bool ShouldProcessRequest(string url)
        {
            return _provider.ShouldProcessRequest(url);
        }

        /// <summary>
        /// Tells you if an incoming request's url should NOT hav its contents Url replaced.
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public static bool ShouldExcludeProcessRequest(string url)
        {
            return _provider.ShouldExcludeRequest(url);
        }

        /// <summary>
        /// Extracts the sitecore media item path from a Url 
        /// </summary>
        /// <param name="localPath">~/media/path/to/file.ashx?w=1</param>
        /// <returns>/sitecore/media library/path/to/file</returns>
        public static string GetMediaItemPath(string localPath)
        {
            return _provider.GetMediaItemPath(localPath);
        }

        /// <summary>
        /// Attempts to retrieve the CDN hostname for the current site
        /// </summary>
        /// <returns></returns>
        public static string GetCDNHostName()
        {
            return _provider.GetCDNHostName();
        }

        /// <summary>
        /// Attempts to retrive the CDN hostname for this site
        /// </summary>
        /// <param name="siteContext"></param>
        /// <returns></returns>
        public static string GetCDNHostName(SiteContext siteContext)
        {
            return _provider.GetCDNHostName(siteContext);
        }

        /// <summary>
        /// Is this media item publicly accessible by the anonymous user?
        /// </summary>
        /// <param name="media"></param>
        /// <returns></returns>
        public static bool IsMediaPubliclyAccessible(MediaItem media)
        {
            return _provider.IsMediaPubliclyAccessible(media);
        }

        /// <summary>
        /// Is this media item Tracked by DMS?
        /// </summary>
        /// <param name="media"></param>
        /// <returns></returns>
        public static bool IsMediaAnalyticsTracked(MediaItem media)
        {
            return _provider.IsMediaAnalyticsTracked(media);
        }

        public static bool IsAjaxRequest()
        {
            return (HttpContext.Current.Request["X-Requested-With"] == "XMLHttpRequest") || (HttpContext.Current.Request.Headers["X-Requested-With"] == "XMLHttpRequest");
        }

        public static string ReplaceMediaUrls(string content)
        {
            if (IsAjaxRequest() && CDNSettings.Enabled && !string.IsNullOrEmpty(GetCDNHostName()))
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(content);

                ReplaceMediaUrls(doc);

                var cdnStringBuilder = new StringBuilder();
                var cdnTextWriter = new StringWriter(cdnStringBuilder);
                var cdnHtmlWriter = new HtmlTextWriter(cdnTextWriter);

                doc.Save(cdnHtmlWriter);

                content = cdnStringBuilder.ToString();
            }

            return content;
        }
    }
}