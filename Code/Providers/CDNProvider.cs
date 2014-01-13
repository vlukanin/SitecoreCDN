namespace NTTData.SitecoreCDN.Providers
{
    using System;
    using System.Linq;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using System.Web;
    using System.Xml.Linq;
    using HtmlAgilityPack;
    using NTTData.SitecoreCDN.Caching;
    using NTTData.SitecoreCDN.Configuration;
    using Sitecore;
    using Sitecore.Configuration;
    using Sitecore.Data.Items;
    using Sitecore.Data.Managers;
    using Sitecore.Diagnostics;
    using Sitecore.Globalization;
    using Sitecore.IO;
    using Sitecore.Reflection;
    using Sitecore.Resources.Media;
    using Sitecore.Security.Domains;
    using Sitecore.Sites;
    using Sitecore.Text;
    using Sitecore.Web;

    /// <summary>
    /// Contains all CDN related provider methods.
    /// </summary>
    public class CDNProvider
    {
        public const string CssUrlRegexPattern = "url\\(\\s*['\"]?([^\"')]+)['\"]?\\s*\\)";

        private UrlCache _cache; // cache url/security/tracking results here
        private ExcludeIncludeCache _excludeUrlCache; // cache url excludes here
        private ExcludeIncludeCache _includeUrlCache; // cache url includes here
        private ExcludeIncludeCache _excludeRequestCache; // cache url request excludes here

        public CDNProvider()
        {
            long cacheSize = StringUtil.ParseSizeString(Settings.GetSetting("SitecoreCDN.FileVersionCacheSize", "5MB"));
            this._cache = new UrlCache("CDNUrl", cacheSize);
            this._excludeUrlCache = new ExcludeIncludeCache("CDNExcludes", cacheSize);
            this._includeUrlCache = new ExcludeIncludeCache("CDNIncludes", cacheSize);
            this._excludeRequestCache = new ExcludeIncludeCache("CDNRequestExcludes", cacheSize);
        }

        /// <summary>
        /// The token used to stop url replacement
        /// </summary>
        public virtual string StopToken
        {
            get { return "ncdn"; }
        }

        /// <summary>
        /// special value to indicate no caching
        /// </summary>
        public virtual string NoCacheToken
        {
            get { return "#nocache#"; }
        }

        /// <summary>
        /// replace appropriate media urls in a full HtmlDocument
        /// </summary>
        /// <param name="doc"></param>
        public virtual void ReplaceMediaUrls(HtmlDocument doc)
        {
            try
            {
                string cdnHostName = this.GetCDNHostName();

                // for any <link href=".." /> do replacement
                var links = doc.DocumentNode.SelectNodes("//link");
                if (links != null)
                {
                    foreach (HtmlNode link in links)
                    {
                        string href = link.GetAttributeValue("href", string.Empty);

                        // don't replace VisitorIdentification.aspx
                        if (!string.IsNullOrEmpty(href) && !this.UrlIsExluded(href))
                        {
                            link.SetAttributeValue("href", this.ReplaceMediaUrl(href, cdnHostName));
                        }
                    }
                }


                HtmlNode scriptTargetNode = null;

                if (CDNSettings.FastLoadJsEnabled)
                {
                    // if <div id='cdn_scripts'></div> exists, append scripts here rather than </body>
                    scriptTargetNode = doc.DocumentNode.SelectSingleNode("//*[@id='cdn_scripts']") ??
                                       doc.DocumentNode.SelectSingleNode("//body");
                }


                var imgscripts = doc.DocumentNode.SelectNodes("//img | //script");

                // for any <img src="..." /> or <script src="..." /> do replacements
                if (imgscripts != null)
                {
                    foreach (HtmlNode element in imgscripts)
                    {
                        string src = element.GetAttributeValue("src", string.Empty);
                        if (!string.IsNullOrEmpty(src) && !this.UrlIsExluded(src))
                        {
                            element.SetAttributeValue("src", this.ReplaceMediaUrl(src, cdnHostName));
                        }

                        // move scripts to scriptTargetNode if FastLoadJsEnabled = true
                        if (element.Name == "script" && scriptTargetNode != null)
                        {
                            scriptTargetNode.AppendChild(element.Clone());
                            element.Remove();
                        }
                    }
                }

                // NOTE: DOREL CHANGE: Change URLs inside style attributes of elements (e.g. style="background-image: url(<...>)")
                var elementsWithUrlInStyle = doc.DocumentNode.SelectNodes("//*[contains(@style, 'url')]");
                if (elementsWithUrlInStyle != null)
                {
                    var regReplaceUrl = new Regex(CssUrlRegexPattern);

                    foreach (HtmlNode element in elementsWithUrlInStyle)
                    {
                        string style = HttpUtility.HtmlDecode(element.GetAttributeValue("style", string.Empty));
                        style = this.ReplaceMediaUrlsByRegex(cdnHostName, style, regReplaceUrl);

                        if (!string.IsNullOrEmpty(style))
                        {
                            element.SetAttributeValue("style", style);
                        }
                    }
                }

                // NOTE: DOREL CHANGE: Change URLs inside <style> elements (e.g. background-image: url(<...>);)
                var styleElements = doc.DocumentNode.SelectNodes("//style");
                if (styleElements != null)
                {
                    var regReplaceUrl = new Regex(CssUrlRegexPattern);

                    foreach (HtmlNode element in styleElements)
                    {
                        string innerHtml = element.InnerHtml;
                        innerHtml = this.ReplaceMediaUrlsByRegex(cdnHostName, innerHtml, regReplaceUrl);

                        if (!string.IsNullOrEmpty(innerHtml))
                        {
                            element.InnerHtml = innerHtml;
                        }
                    }
                }

                // NOTE: DOREL CHANGE: Change URLs for all href attributes of <a> tags that contain media URLs (e.g., links to PDF documents)
                string mediaLinkPrefixWithDash = Settings.Media.MediaLinkPrefix.Replace("~", "-");
                string mediaLinkPrefixWithTilde = Settings.Media.MediaLinkPrefix.Replace("-", "~");

                string mediaLinkPrefixWithDashEnsurePrefix = StringUtil.EnsurePrefix('/', mediaLinkPrefixWithDash);
                string mediaLinkPrefixWithTildeEnsurePrefix = StringUtil.EnsurePrefix('/', mediaLinkPrefixWithTilde);

                string mediaLinkPrefixWithDashWithoutPrefix = mediaLinkPrefixWithDashEnsurePrefix.Substring(1, mediaLinkPrefixWithDashEnsurePrefix.Length - 1);
                string mediaLinkPrefixWithTildeWithoutPrefix = mediaLinkPrefixWithTildeEnsurePrefix.Substring(1, mediaLinkPrefixWithTildeEnsurePrefix.Length - 1);

                var anchors = doc.DocumentNode.SelectNodes(
                    string.Format(
                        "//a[contains(@href, '{0}')] | //a[contains(@href, '{1}')] | //a[contains(@href, '{2}')] | //a[contains(@href, '{3}')]",
                        mediaLinkPrefixWithDashEnsurePrefix,
                        mediaLinkPrefixWithTildeEnsurePrefix,
                        mediaLinkPrefixWithDashWithoutPrefix,
                        mediaLinkPrefixWithTildeWithoutPrefix));

                if (anchors != null)
                {
                    foreach (HtmlNode element in anchors)
                    {
                        string href = element.GetAttributeValue("href", string.Empty);
                        if (!string.IsNullOrEmpty(href) && !this.UrlIsExluded(href))
                        {
                            element.SetAttributeValue("href", this.ReplaceMediaUrl(href, cdnHostName));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("ReplaceMediaUrls", ex, this);
            }
        }

        /// <summary>
        /// Rewrites media urls to point to CDN hostname and dehydrates querystring into filename
        /// </summary>
        /// <param name="inputUrl">/path/to/file.ext?a=1&b=2</param>
        /// <returns>http://cdnHostname/path/to/file!cf!a=1!b=2.ext</returns>
        public virtual string ReplaceMediaUrl(string inputUrl, string cdnHostname)
        {
            // string versionKey = inputUrl + "_v";
            // string updatedKey = inputUrl + "_d";
            string cachedKey = string.Concat(WebUtil.GetScheme(), inputUrl);
            try
            {
                string cachedUrl = this._cache.GetUrl(cachedKey);

                if (!string.IsNullOrEmpty(cachedUrl))
                {
                    return cachedUrl;
                }

                // ignore fully qualified urls or data:
                if (WebUtil.IsExternalUrl(inputUrl) || inputUrl.StartsWith("data:") || inputUrl.StartsWith("//"))
                {
                    return inputUrl;
                }

                UrlString url = new UrlString(WebUtil.NormalizeUrl(inputUrl));
                UrlString originalUrl = new UrlString(WebUtil.NormalizeUrl(inputUrl));

                // if the stoptoken ex. ?nfc=1  is non-empty, don't replace this url
                if (!string.IsNullOrEmpty(url[this.StopToken]))
                {
                    url.Remove(this.StopToken);
                }
                else
                {
                    if (!string.IsNullOrEmpty(cdnHostname))
                    {
                        url.HostName = cdnHostname; // insert CDN hostname
                    }

                    if (CDNSettings.MatchProtocol)
                    {
                        url.Protocol = WebUtil.GetScheme();
                    }

                    url.Path = StringUtil.EnsurePrefix('/', url.Path);  // ensure first "/" before ~/media


                    if (CDNSettings.FilenameVersioningEnabled)
                    {
                        // NOTE: DOREL CHANGE: THIS CHANGE IS NEEDED TO ADDING PARAMS TO MEDIA THAT HAVE "/~/media/"
                        string mediaLinkPrefixWithDash = Settings.Media.MediaLinkPrefix.Replace("~", "-");
                        string mediaLinkPrefixWithTilde = Settings.Media.MediaLinkPrefix.Replace("-", "~");

                        // NOTE: DOREL CHANGE: use url.Path instead of inputUrl, because url.Path already start with "/" anyway (because of previous EnsurePrefix)
                        // if this is a media library request
                        if (url.Path.Contains(mediaLinkPrefixWithDash) || url.Path.Contains(mediaLinkPrefixWithTilde))
                        {
                            string version = url["vs"] ?? string.Empty;
                            string updated = string.Empty;

                            // get sitecore path of media item
                            string mediaItemPath = this.GetMediaItemPath(url.Path);
                            if (!string.IsNullOrEmpty(mediaItemPath) && Sitecore.Context.Database != null)
                            {
                                Item mediaItem = string.IsNullOrEmpty(version)
                                                     ? Sitecore.Context.Database.SelectSingleItem(mediaItemPath)
                                                     : Sitecore.Context.Database.GetItem(mediaItemPath, Sitecore.Context.Language, Sitecore.Data.Version.Parse(version));

                                if (mediaItem == null)
                                {
                                    // no change to url
                                    url = originalUrl;
                                }
                                else
                                {
                                    // do not replace url if media item isn't public or requires Analytics processing
                                    // keep local url for this case
                                    if (!this.IsMediaPubliclyAccessible(mediaItem) || this.IsMediaAnalyticsTracked(mediaItem))
                                    {
                                        // no change to url
                                        url = originalUrl;
                                    }
                                    else
                                    {
                                        version = mediaItem.Version.Number.ToString();

                                        // NOTE: DOREL CHANGE: if media item doesn't have corresponding language version, then get statistic from existing language version
                                        if (!mediaItem.Statistics.Updated.Equals(DateTime.MinValue))
                                        {
                                            updated = DateUtil.ToIsoDate(mediaItem.Statistics.Updated);
                                        }
                                        else
                                        {
                                            Language languageInternational = LanguageManager.GetLanguage("en");

                                            Item mediaItemInternational = string.IsNullOrEmpty(version)
                                                     ? Sitecore.Context.Database.GetItem(mediaItemPath, languageInternational)
                                                     : Sitecore.Context.Database.GetItem(mediaItemPath, languageInternational, Sitecore.Data.Version.Parse(version));

                                            if (mediaItemInternational != null && !mediaItemInternational.Statistics.Updated.Equals(DateTime.MinValue))
                                            {
                                                updated = DateUtil.ToIsoDate(mediaItemInternational.Statistics.Updated);
                                            }
                                            else
                                            {
                                                foreach (Language language in mediaItem.Languages)
                                                {
                                                    Item mediaItemLanguage = string.IsNullOrEmpty(version)
                                                        ? Sitecore.Context.Database.GetItem(mediaItemPath, language)
                                                        : Sitecore.Context.Database.GetItem(mediaItemPath, language, Sitecore.Data.Version.Parse(version));

                                                    if (mediaItemLanguage != null && !mediaItemLanguage.Statistics.Updated.Equals(DateTime.MinValue))
                                                    {
                                                        updated = DateUtil.ToIsoDate(mediaItemLanguage.Statistics.Updated);
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            if (!string.IsNullOrEmpty(version))
                            {
                                // append version number qs
                                url.Add("vs", version);
                            }

                            if (!string.IsNullOrEmpty(updated))
                            {
                                // append  timestamp qs
                                url.Add("d", updated);
                            }
                        }
                        else // else this is a static file url
                        {
                            string updated = string.Empty;

                            if (FileUtil.FileExists(url.Path))
                            {
                                DateTime lastWrite = FileUtil.GetFileWriteTime(url.Path);
                                updated = DateUtil.ToIsoDate(lastWrite);
                            }

                            if (!string.IsNullOrEmpty(updated))
                            {
                                // append timestamp qs
                                url.Add("d", updated);
                            }

                            if (CDNSettings.MinifyEnabled && (url.Path.EndsWith(".css") || url.Path.EndsWith(".js")))
                            {
                                url.Add("min", "1");
                            }
                        }
                    }
                }

                string outputUrl = url.ToString().TrimEnd('?'); // prevent trailing ? with blank querystring

                this._cache.SetUrl(cachedKey, outputUrl);

                return outputUrl;
            }
            catch (Exception ex)
            {
                Log.Error(string.Format("ReplaceMediaUrl {0} {1}", cdnHostname, inputUrl), ex, this);
                return inputUrl;
            }
        }

        /// <summary>
        /// Tells you if the url is excluded by ExcludeUrlPatterns in .config
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public virtual bool UrlIsExluded(string url)
        {
            bool? exc = this._excludeUrlCache.GetResult(url);
            if (exc.HasValue)
            {
                return exc.Value;
            }

            bool output = CDNSettings.ExcludeUrlPatterns.Any(re => re.IsMatch(url));
            this._excludeUrlCache.SetResult(url, output);
            return output;
        }

        /// <summary>
        /// Tells you if an incoming request's url should have it's contents Url replaced.
        /// ProcessRequestPatterns in .config
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public virtual bool ShouldProcessRequest(string url)
        {
            bool? inc = this._includeUrlCache.GetResult(url);
            if (inc.HasValue)
            {
                return inc.Value;
            }

            bool output = CDNSettings.ProcessRequestPatterns.Any(re => re.IsMatch(url));
            this._includeUrlCache.SetResult(url, output);
            return output;
        }

        /// <summary>
        /// Tells you if an incoming request's url should NOT hav its contents Url replaced.
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        public virtual bool ShouldExcludeRequest(string url)
        {
            bool? exc = this._excludeRequestCache.GetResult(url);
            if (exc.HasValue)
            {
                return exc.Value;
            }

            bool output = CDNSettings.ExcludeRequestPatterns.Any(re => re.IsMatch(url));
            this._excludeRequestCache.SetResult(url, output);
            return output;
        }


        /// <summary>
        /// Extracts the sitecore media item path from a Url 
        /// </summary>
        /// <param name="localPath">~/media/path/to/file.ashx?w=1</param>
        /// <returns>/sitecore/media library/path/to/file</returns>
        public virtual string GetMediaItemPath(string localPath)
        {
            var mr = new MediaRequest();

            // this is a hack to access a private method in MediaRequest
            MethodInfo mi = ReflectionUtil.GetMethod(mr, "GetMediaPath", true, true, new object[] { localPath });
            if (mi == null)
            {
                return null;
            }

            var result = (string)ReflectionUtil.InvokeMethod(mi, new object[] { localPath }, mr);

            // NOTE: DOREL CHANGE: replace %20 with white-space
            return !string.IsNullOrEmpty(result) ? result.Replace("%20", " ") : result;
        }


        /// <summary>
        /// Attempts to retrieve the CDN hostname for the current site
        /// </summary>
        /// <returns></returns>
        public virtual string GetCDNHostName()
        {
            return this.GetCDNHostName(Sitecore.Context.Site);
        }

        /// <summary>
        /// Attempts to retrive the CDN hostname for this site
        /// </summary>
        /// <param name="siteContext"></param>
        /// <returns></returns>
        public virtual string GetCDNHostName(SiteContext siteContext)
        {
            if (siteContext == null)
            {
                return string.Empty;
            }

            // try to find <site name='[sitename]'  cdnHostName='[cdnhostname]' />
            return StringUtil.GetString(new string[] { siteContext.Properties.Get("cdnHostName") });
        }

        /// <summary>
        /// Is this media item publicly accessible by the anonymous user?
        /// </summary>
        /// <param name="media"></param>
        /// <returns></returns>
        public virtual bool IsMediaPubliclyAccessible(MediaItem media)
        {
            string cacheKey = media.ID.ToString() + "_public";
            string cached = this._cache.GetUrl(cacheKey);
            bool output = true;

            // cached result
            if (!string.IsNullOrEmpty(cached))
            {
                output = MainUtil.GetBool(cached, true);
            }
            else
            {
                Domain domain = Sitecore.Context.Domain ?? Factory.GetDomain("extranet");
                var anon = domain.GetAnonymousUser();
                if (anon != null)
                {
                    output = media.InnerItem.Security.CanRead(anon);
                }

                this._cache.SetUrl(cacheKey, output.ToString());
            }

            return output;
        }

        /// <summary>
        /// Is this media item Tracked by DMS?
        /// </summary>
        /// <param name="media"></param>
        /// <returns></returns>
        public virtual bool IsMediaAnalyticsTracked(MediaItem media)
        {
            try
            {
                if (!Settings.Analytics.Enabled)
                {
                    return false;
                }

                string cacheKey = media.ID.ToString() + "_tracked";
                string cached = this._cache.GetUrl(cacheKey);
                bool output = false;

                // cached result
                if (!string.IsNullOrEmpty(cached))
                {
                    output = MainUtil.GetBool(cached, true);
                }
                else
                {
                    string trackingData = media.InnerItem["__Tracking"];

                    if (string.IsNullOrEmpty(trackingData))
                    {
                        output = false;
                    }
                    else
                    {
                        XElement el = XElement.Parse(trackingData);
                        var ignore = el.Attribute("ignore");

                        if (ignore != null && ignore.Value == "1")
                        {
                            output = false;
                        }
                        else
                        {
                            // if the tracking element has any events, campaigns or profiles.
                            output = el.Elements("event").Any() || el.Elements("campaign").Any() || el.Elements("profile").Any();
                        }
                    }

                    this._cache.SetUrl(cacheKey, output.ToString());
                }

                return output;
            }
            catch (Exception ex)
            {
                Log.Error("IsMediaAnalyticsTracked", ex, this);
                return false;
            }
        }

        /// <summary>
        /// Replace media URLs inside input string by regex.
        /// </summary>
        private string ReplaceMediaUrlsByRegex(string cdnHostName, string input, Regex regReplaceUrl)
        {
            if (!string.IsNullOrEmpty(input))
            {
                return regReplaceUrl.Replace(
                    input,
                    (m) =>
                    {
                        string oldUrl = string.Empty;
                        if (m.Groups.Count > 1)
                        {
                            oldUrl = m.Groups[1].Value;
                        }

                        if (!oldUrl.Contains("://") && oldUrl.Contains(":/"))
                        {
                            return m.Value.Replace(oldUrl, oldUrl.Replace(":/", "://"));
                        }

                        if (WebUtil.IsInternalUrl(oldUrl))
                        {
                            string newUrl = this.ReplaceMediaUrl(oldUrl, cdnHostName);
                            if (!string.IsNullOrEmpty(newUrl))
                            {
                                return m.Value.Replace(m.Groups[1].Value, newUrl);
                            }
                        }

                        return m.Value;
                    });
            }

            return input;
        }
    }
}
