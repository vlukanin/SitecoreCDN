namespace NTTData.SitecoreCDN.Filters
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using HtmlAgilityPack;
    using NTTData.SitecoreCDN.Configuration;
    using NTTData.SitecoreCDN.Util;
    using Sitecore.Diagnostics;
    using Sitecore.Web;

    /// <summary>
    /// A filter stream that allows the replacing of img/script src attributes (and link tag's href attribute) 
    /// with CDN appended urls
    /// 
    /// i.e.   "~/media/path/to/file.ashx?w=400&h=200"  becomes "http://mycdnhostname/~/media/path/to/file.ashx?w=400&h=200&v=2&d=20130101T000000"
    /// 
    /// </summary>
    public class MediaUrlFilter : Stream
    {
        private Stream _responseStream;
        private StringBuilder _sb;
        private bool _isComplete;

        public MediaUrlFilter(Stream inputStream)
        {
            this._responseStream = inputStream;
            this._sb = new StringBuilder();
            this._isComplete = false;
        }


        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return true; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }

        public override void Flush()
        {
            // if the stream wasn't completed by Write, output the contents of the inner stream first
            if (!this._isComplete)
            {
                byte[] data = Encoding.UTF8.GetBytes(this._sb.ToString());
                this._responseStream.Write(data, 0, data.Length);
            }

            this._responseStream.Flush();
        }

        public override void Close()
        {
            this._responseStream.Close();
        }

        public override long Length
        {
            get { return 0; }
        }

        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return this._responseStream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return this._responseStream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            this._responseStream.SetLength(value);
        }

        /// <summary>
        /// This Method buffers the original Write payloads until the end of the end [/html] tag
        /// when replacement occurs
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            // preview the contents of the payload
            string content = Encoding.UTF8.GetString(buffer, offset, count);

            var eof = new Regex("</html>", RegexOptions.IgnoreCase);

            // if the content contains </html> we know we're at the end of the line
            // otherwise append the contents to the stringbuilder
            if (!eof.IsMatch(content))
            {
                if (this._isComplete)
                {
                    this._responseStream.Write(buffer, offset, count);
                }
                else
                {
                    this._sb.Append(content);
                }
            }
            else
            {
                this._sb.Append(content.Substring(0, content.IndexOf("</html>", StringComparison.Ordinal) + 7));

                string extra = content.Substring(content.IndexOf("</html>", StringComparison.Ordinal) + 7);

                this.ReplaceMediaUrls();

                if (!string.IsNullOrEmpty(extra))
                {
                    byte[] data = Encoding.UTF8.GetBytes(extra);
                    this._responseStream.Write(data, 0, data.Length);
                }
            }
        }

        private void ReplaceMediaUrls()
        {
            try
            {
                using (new TimerReport("replaceMediaUrls"))
                {
                    // parse complete document into HtmlDocument
                    var doc = new HtmlDocument();
                    doc.LoadHtml(this._sb.ToString());

                    if (CDNSettings.DebugParser)
                    {
                        var parseErrors = doc.ParseErrors;
                        if (parseErrors != null)
                        {
                            parseErrors =
                                parseErrors.Where(
                                    pe =>
                                    pe.Code == HtmlParseErrorCode.EndTagInvalidHere ||
                                    pe.Code == HtmlParseErrorCode.TagNotClosed ||
                                    pe.Code == HtmlParseErrorCode.TagNotOpened);
                        }

                        if (parseErrors != null && parseErrors.Any())
                        {
                            var sb = new StringBuilder();
                            foreach (var parseError in parseErrors)
                            {
                                sb.AppendLine(string.Format("PARSE ERROR: {0}", parseError.Reason));
                                sb.AppendLine(string.Format("Line: {0} Position: {1}", parseError.Line, parseError.LinePosition));
                                sb.AppendLine(string.Format("Source: {0}", parseError.SourceText));
                                sb.AppendLine(string.Empty);
                            }

                            Log.Error(string.Format("CDN Url Parsing Error - URL: {0} {1} {2}", WebUtil.GetRawUrl(), Environment.NewLine, sb), this);
                        }
                    }

                    // replace appropriate urls
                    CDNManager.ReplaceMediaUrls(doc);

                    var writer = new StreamWriter(this._responseStream);
                    doc.Save(writer);
                    writer.Flush();
                }

                this._isComplete = true;
            }
            catch (Exception ex)
            {
                Log.Error("CDN MediaURL Filter Error", ex, this);
            }
        }
    }
}
