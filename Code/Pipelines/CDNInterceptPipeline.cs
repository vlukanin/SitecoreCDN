namespace NTTData.SitecoreCDN.Pipelines
{
    using Sitecore.Pipelines.PreprocessRequest;
    using Sitecore.Diagnostics;
    using Sitecore.Configuration;
    using NTTData.SitecoreCDN.Configuration;
    using Sitecore.Text;

    public class CDNInterceptPipeline : PreprocessRequestProcessor
    {
        /// <summary>
        /// rewrite CDN urls from  /path/to/file!cf!a=1!b=2.ext to original form /path/to/file.ext?a=1&b=2
        /// </summary>
        /// <param name="args"></param>
        public override void Process(PreprocessRequestArgs args)
        {
            Assert.ArgumentNotNull(args, "args");

            // rehydrate original url
            string fullPath = Sitecore.Context.RawUrl;
            var url = new UrlString(fullPath);

            // if this item is a minifiable css or js
            // rewrite for ~/minify handler
            if (CDNSettings.Enabled &&
                CDNSettings.MinifyEnabled &&
                url["min"] == "1" &&
                !url.Path.StartsWith(Settings.Media.DefaultMediaPrefix) &&
                (url.Path.EndsWith(".css") || url.Path.EndsWith(".js")))
            {
                args.Context.Items["MinifyPath"] = fullPath;   // set this for the Minifier handler
                args.Context.RewritePath("/~/minify" + url.Path, string.Empty, url.Query);  // rewrite with ~/minify to trigger custom handler
            }

            // NOTE: DOREL CHANGE: Commented to make WCF services (*.svc) workable
//            else
//            {
//                args.Context.RewritePath(url.Path, string.Empty, url.Query); // rewrite proper url
//            }
        }
    }
}
