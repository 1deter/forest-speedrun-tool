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

        public static IEnumerator Send(string method, string url, byte[] body, string contentType,
                                       string bearer, float timeoutSeconds, Done done)
        {
            Type requestType = Type.GetType("UnityEngine.Networking.UnityWebRequest, UnityEngine");
            Type uploadType = Type.GetType("UnityEngine.Networking.UploadHandlerRaw, UnityEngine");
            Type downloadType = Type.GetType("UnityEngine.Networking.DownloadHandlerBuffer, UnityEngine");
            if (requestType == null || uploadType == null || downloadType == null)
            {
                done(0, null, "UnityWebRequest unavailable in this build");
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

                MethodInfo send = requestType.GetMethod("Send") ?? requestType.GetMethod("SendWebRequest");
                send.Invoke(request, null);
            }
            catch (Exception ex)
            {
                done(0, null, "request failed: " + (ex.InnerException ?? ex).Message);
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
                    done(0, null, "timed out");
                    yield break;
                }
                yield return null;
            }

            long code = 0;
            string text = null, error = null;
            try
            {
                PropertyInfo errorProp = requestType.GetProperty("error");
                error = errorProp != null ? errorProp.GetValue(request, null) as string : null;
                PropertyInfo codeProp = requestType.GetProperty("responseCode");
                if (codeProp != null) code = Convert.ToInt64(codeProp.GetValue(request, null));
                object handler = requestType.GetProperty("downloadHandler").GetValue(request, null);
                byte[] data = handler != null ? handler.GetType().GetProperty("data").GetValue(handler, null) as byte[] : null;
                if (data != null) text = Encoding.UTF8.GetString(data);
            }
            catch (Exception ex) { error = ex.Message; }
            Dispose(requestType, request);

            // An answer with a status is not a network error, whatever
            // Unity's error string says about it.
            done(code, text, code != 0 ? null : (string.IsNullOrEmpty(error) ? "no answer" : error));
        }

        private static void Dispose(Type requestType, object request)
        {
            try { requestType.GetMethod("Dispose").Invoke(request, null); }
            catch (Exception) { }
        }
    }
}
