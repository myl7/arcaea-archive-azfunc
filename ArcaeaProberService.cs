using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace arcaea_archive_azfunc
{
    public static class ArcaeaProberService
    {
        private const string ArcaeaProberUrl = "wss://arc.estertion.win:616";
        private const string Query = "984569312 7 12";

        public static async Task<List<string>> Get()
        {
            var url = new Uri(ArcaeaProberUrl);
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(url, CancellationToken.None);

            var util = new WebSocketUtils(ws);
            await util.SendTextAsync(Query);
            var queriedReply = Encoding.ASCII.GetString(await util.ReceiveMsgAsync(WebSocketMessageType.Text));
            if (queriedReply != "queried")
            {
                throw new ArchiveException(
                    $"ArcaeaProberGet fail: Query replies {queriedReply} other than queried");
            }

            var decUrl = Environment.GetEnvironmentVariable("BROTLI_DEC_AZFUNC_URL");
            var results = new List<string>();
            var tasks = new List<Task>();

            async Task BrotliDec(byte[] data, int i)
            {
                var req = WebRequest.Create(decUrl!);
                req.Method = WebRequestMethods.Http.Post;
                req.ContentType = "application/octet-stream";
                req.ContentLength = data.Length;
                await using var reqStream = await req.GetRequestStreamAsync();
                await reqStream.WriteAsync(data);

                var res = await req.GetResponseAsync() as HttpWebResponse;
                await using var resStream = res!.GetResponseStream();
                var reader = new StreamReader(resStream!, Encoding.UTF8);
                results[i] = await reader.ReadToEndAsync();
            }

            while (true)
            {
                try
                {
                    var data = await util.ReceiveMsgAsync();
                    var i = results.Count;
                    results.Add("");
                    var task = BrotliDec(data, i);
                    tasks.Add(task);
                }
                catch (WebSocketUtils.WebSocketUtilException e)
                {
                    if (e.I != 0 || e.Type != WebSocketMessageType.Text ||
                        Encoding.ASCII.GetString(e.Buf, 0, e.MsgSize) != "bye")
                    {
                        throw new ArchiveException("ArcaeaProberGet fail: Exit without bye");
                    }

                    await Task.WhenAll(tasks);
                    return results;
                }
            }
        }
    }

    internal class WebSocketUtils
    {
        private WebSocket Ws { get; }
        private const int BufSize = 4095;

        public WebSocketUtils(WebSocket ws)
        {
            Ws = ws;
        }

        public async Task SendTextAsync(string text)
        {
            await Ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true,
                CancellationToken.None);
        }

        public async Task<byte[]> ReceiveMsgAsync(WebSocketMessageType type = WebSocketMessageType.Binary)
        {
            var buf = new byte[BufSize];
            var i = 0;
            while (true)
            {
                var slice = new ArraySegment<byte>(buf, i, BufSize - i);
                var res = await Ws.ReceiveAsync(slice, CancellationToken.None);
                if (res.MessageType != type)
                {
                    throw new WebSocketUtilException(buf, i, res.MessageType, res.Count);
                }

                i += res.Count;
                if (res.EndOfMessage)
                {
                    break;
                }
            }

            return buf.Take(i).ToArray();
        }

        public class WebSocketUtilException : Exception
        {
            public byte[] Buf { get; }
            public int I { get; }
            public WebSocketMessageType Type { get; }
            public int MsgSize { get; }

            public WebSocketUtilException(byte[] buf, int i, WebSocketMessageType type, int msgSize)
            {
                Buf = buf;
                I = i;
                Type = type;
                MsgSize = msgSize;
            }
        }
    }
}
