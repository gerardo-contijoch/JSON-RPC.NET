﻿using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace AustinHarris.JsonRpc
{
    public static class JsonRpcProcessor
    {
        public static void Process(JsonRpcStateAsync async, object context = null,
            JsonSerializerSettings settings = null)
        {
            Process(Handler.DefaultSessionId(), async, context, settings);
        }

        public static void Process(string sessionId, JsonRpcStateAsync async, object context = null,
            JsonSerializerSettings settings = null)
        {
            Process(sessionId, async.JsonRpc, context, settings)
                .ContinueWith(t =>
                {
                    async.Result = t.Result;
                    async.SetCompleted();
                });
        }

        public static Task<string> Process(string jsonRpc, object context = null,
            JsonSerializerSettings settings = null)
        {
            return Process(Handler.DefaultSessionId(), jsonRpc, context, settings);
        }

        public static Task<string> Process(string sessionId, string jsonRpc, object context = null,
            JsonSerializerSettings settings = null)
        {
            return Task<string>.Factory.StartNew((_) =>
            {
                var tuple = (Tuple<string, string, object, JsonSerializerSettings>)_;
                return ProcessSync(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4);
            }, new Tuple<string, string, object, JsonSerializerSettings>(sessionId, jsonRpc, context, settings));
        }

        public static string ProcessSync(string sessionId, string jsonRpc, object jsonRpcContext,
            JsonSerializerSettings settings = null)
        {
            var handler = Handler.GetSessionHandler(sessionId);


            JsonRequest[] batch = null;
            try
            {
                if (isSingleRpc(jsonRpc))
                {
                    var foo = JsonConvert.DeserializeObject<JsonRequest>(jsonRpc, settings);
                    batch = new[] { foo };
                }
                else
                {
                    batch = JsonConvert.DeserializeObject<JsonRequest[]>(jsonRpc, settings);
                }
            }
            catch (Exception ex)
            {
                return Newtonsoft.Json.JsonConvert.SerializeObject(new JsonResponse
                {
                    Error = handler.ProcessParseException(jsonRpc, new JsonRpcException(-32700, "Parse error", ex))
                }, settings);
            }

            if (batch.Length == 0)
            {
                return Newtonsoft.Json.JsonConvert.SerializeObject(new JsonResponse
                {
                    Error = handler.ProcessParseException(jsonRpc,
                        new JsonRpcException(3200, "Invalid Request", "Batch of calls was empty."))
                }, settings);
            }

            var singleBatch = batch.Length == 1;
            StringBuilder sbResult = null;
            for (var i = 0; i < batch.Length; i++)
            {
                var jsonRequest = batch[i];
                var jsonResponse = new JsonResponse();

                if (jsonRequest == null)
                {
                    jsonResponse.Error = handler.ProcessParseException(jsonRpc,
                        new JsonRpcException(-32700, "Parse error",
                            "Invalid JSON was received by the server. An error occurred on the server while parsing the JSON text."));
                }
                else if (jsonRequest.Method == null)
                {
                    jsonResponse.Error = handler.ProcessParseException(jsonRpc,
                        new JsonRpcException(-32600, "Invalid Request", "Missing property 'method'"));
                }
                else if (!isSimpleValueType(jsonRequest.Id))
                {
                    jsonResponse.Error = handler.ProcessParseException(jsonRpc,
                        new JsonRpcException(-32600, "Invalid Request", "Id property must be either null or string or integer."));
                }
                else
                {
                    jsonResponse.Id = jsonRequest.Id;

                    var data = handler.Handle(jsonRequest, jsonRpcContext);

                    if (data == null) continue;

                    jsonResponse.JsonRpc = data.JsonRpc;
                    jsonResponse.Error = data.Error;
                    jsonResponse.Result = data.Result;

                }
                if (jsonResponse.Result == null && jsonResponse.Error == null)
                {
                    // Per json rpc 2.0 spec
                    // result : This member is REQUIRED on success.
                    // This member MUST NOT exist if there was an error invoking the method.    
                    // Either the result member or error member MUST be included, but both members MUST NOT be included.
                    jsonResponse.Result = new Newtonsoft.Json.Linq.JValue((Object)null);
                }
                // special case optimization for single Item batch
                if (singleBatch && (jsonResponse.Id != null || jsonResponse.Error != null))
                {
                    StringWriter sw = new StringWriter();
                    JsonTextWriter writer = new JsonTextWriter(sw);
                    writer.WriteStartObject();
                    if (!string.IsNullOrEmpty(jsonResponse.JsonRpc))
                    {
                        writer.WritePropertyName("jsonrpc"); writer.WriteValue(jsonResponse.JsonRpc);
                    }
                    if (jsonResponse.Error != null)
                    {
                        writer.WritePropertyName("error"); writer.WriteRawValue(JsonConvert.SerializeObject(jsonResponse.Error));
                    }
                    else
                    {
                        writer.WritePropertyName("result"); writer.WriteRawValue(JsonConvert.SerializeObject(jsonResponse.Result));
                    }
                    writer.WritePropertyName("id"); writer.WriteValue(jsonResponse.Id);
                    writer.WriteEndObject();
                    return sw.ToString();

                    //return JsonConvert.SerializeObject(jsonResponse);
                }
                else if (jsonResponse.Id == null && jsonResponse.Error == null)
                {
                    // do nothing
                    sbResult = new StringBuilder(0);
                }
                else
                {
                    // write out the response
                    if (i == 0)
                    {
                        sbResult = new StringBuilder("[");
                    }

                    sbResult.Append(JsonConvert.SerializeObject(jsonResponse, settings));
                    if (i < batch.Length - 1)
                    {
                        sbResult.Append(',');
                    }
                    else if (i == batch.Length - 1)
                    {
                        sbResult.Append(']');
                    }
                }
            }
            return sbResult.ToString();
        }

        private static bool isSingleRpc(string json)
        {
            for (int i = 0; i < json.Length; i++)
            {
                if (json[i] == '{') return true;
                else if (json[i] == '[') return false;
            }
            return true;
        }

        private static bool isSimpleValueType(object property)
        {
            if (property == null)
                return true;
            return property.GetType() == typeof(System.String) ||
                property.GetType() == typeof(System.Int64) ||
                property.GetType() == typeof(System.Int32) ||
                property.GetType() == typeof(System.Int16);
        }
    }
}
