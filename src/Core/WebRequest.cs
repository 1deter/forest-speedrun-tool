using System;
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace ForestOverlay.Core
{
    // ------------------------------------------------------------------
    // One HTTP request with a body, through Unity's UnityWebRequest (Mono's
    // own TLS cannot reach modern HTTPS - see UpdateChecker). Reflection as
    // there: the CI stub may not expose the type, and an API rename degrades
    // to an error message rather than a build break.
    //
    // Unity 5.6 does not report an HTTP error status as an error: the
    // callback gets the status code (0 = no answer) and the body text, and
    // the caller decides (Data/SiteProtocol.Classify).
    // ------------------------------------------------------------------
    public static class WebRequest
    {
        public delegate void Done(long code, string body, string error);

        /// Done with one response header's value (null: not sent).
        public delegate void DoneHeader(long code, string body, string error, string header);

        public static IEnumerator Send(string method, string url, byte[] body, string contentType,
                                       string bearer, float timeoutSeconds, Done done)
        {
            return Send(method, url, body, contentType, bearer, timeoutSeconds, null, null,
                        delegate(long c, string b, string e, string h) { done(c, b, e); });
        }

        /// With extra request headers (name, value, name, value...) and one
        /// response header read back (`readHeader`, e.g. "ETag").
        public static IEnumerator Send(string method, string url, byte[] body, string contentType, string bearer,
                                       float timeoutSeconds, string[] headers, string readHeader, DoneHeader done)
        {
            Type requestType = Type.GetType("UnityEngine.Networking.UnityWebRequest, UnityEngine");
            Type uploadType = Type.GetType("UnityEngine.Networking.UploadHandlerRaw, UnityEngine");
            Type downloadType = Type.GetType("UnityEngine.Networking.DownloadHandlerBuffer, UnityEngine");
            if (requestType == null || uploadType == null || downloadType == null)
            {
                done(0, null, "UnityWebRequest unavailable in this build", null);
                yield break;
            }

            object request;
            try
            {
                request = Activator.CreateInstance(requestType, new object[] { url, method });
                if (body != null)
                {
                    object up = Activator.CreateInstance(uploadType, new object[] { body });
                    PropertyInfo ct = uploadType.GetProperty("contentType");
                    if (ct != null && ct.CanWrite) ct.SetValue(up, contentType, null);
                    requestType.GetProperty("uploadHandler").SetValue(request, up, null);
                }
                requestType.GetProperty("downloadHandler").SetValue(request, Activator.CreateInstance(downloadType), null);

                MethodInfo setHeader = requestType.GetMethod("SetRequestHeader");
                setHeader.Invoke(request, new object[] { "User-Agent", "ForestOverlay/" + OverlayPlugin.PluginVersion });
                if (body != null) setHeader.Invoke(request, new object[] { "Content-Type", contentType });
                if (!string.IsNullOrEmpty(bearer)) setHeader.Invoke(request, new object[] { "Authorization", "Bearer " + bearer });
                if (headers != null)
                    for (int i = 0; i + 1 < headers.Length; i += 2)
                        if (!string.IsNullOrEmpty(headers[i + 1])) setHeader.Invoke(request, new object[] { headers[i], headers[i + 1] });

                MethodInfo send = requestType.GetMethod("Send") ?? requestType.GetMethod("SendWebRequest");
                send.Invoke(request, null);
            }
            catch (Exception ex)
            {
                done(0, null, "request failed: " + (ex.InnerException ?? ex).Message, null);
                yield break;
            }

            PropertyInfo isDone = requestType.GetProperty("isDone");
            float timeout = Time.unscaledTime + timeoutSeconds;
            while (!(bool)isDone.GetValue(request, null))
            {
                if (Time.unscaledTime > timeout)
                {
                    try { requestType.GetMethod("Abort").Invoke(request, null); } catch (Exception) { }
                    Dispose(requestType, request);
                    done(0, null, "timed out", null);
                    yield break;
                }
                yield return null;
            }

            long code = 0;
            string text = null, error = null, header = null;
            try
            {
                PropertyInfo errorProp = requestType.GetProperty("error");
                error = errorProp != null ? errorProp.GetValue(request, null) as string : null;
                PropertyInfo codeProp = requestType.GetProperty("responseCode");
                if (codeProp != null) code = Convert.ToInt64(codeProp.GetValue(request, null));
                object handler = requestType.GetProperty("downloadHandler").GetValue(request, null);
                byte[] data = handler != null ? handler.GetType().GetProperty("data").GetValue(handler, null) as byte[] : null;
                if (data != null) text = Encoding.UTF8.GetString(data);
                if (readHeader != null)
                {
                    MethodInfo get = requestType.GetMethod("GetResponseHeader", new[] { typeof(string) });
                    if (get != null) header = get.Invoke(request, new object[] { readHeader }) as string;
                }
            }
            catch (Exception ex) { error = ex.Message; }
            Dispose(requestType, request);

            // An answer with a status is not a network error, whatever
            // Unity's error string says about it.
            done(code, text, code != 0 ? null : (string.IsNullOrEmpty(error) ? "no answer" : error), header);
        }

        private static void Dispose(Type requestType, object request)
        {
            try { requestType.GetMethod("Dispose").Invoke(request, null); }
            catch (Exception) { }
        }
    }
}
