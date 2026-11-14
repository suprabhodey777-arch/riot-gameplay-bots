// TCP link to the bridge: NDJSON over 127.0.0.1:7777. A background thread
// connects and reads lines; parsed messages wait in a queue that the main
// thread drains each frame. Sends happen on the caller's thread under a lock
// (one reader thread and one writer at a time is safe on a NetworkStream).
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Saltworks
{
    public enum LinkState
    {
        Idle,
        Connecting,
        Connected,
        Failed,
        Closed,
    }

    public sealed class BridgeClient : IDisposable
    {
        public const string DefaultHost = "127.0.0.1";
        public const int DefaultPort = 7777;
        const int ConnectTimeoutMs = 3000;

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        readonly ConcurrentQueue<JObject> inbox = new ConcurrentQueue<JObject>();
        readonly object sendLock = new object();

        volatile LinkState state = LinkState.Idle;
        volatile string lastError = "";
        int generation;
        TcpClient tcp;
        NetworkStream stream;

        public string Host { get; private set; } = DefaultHost;
        public int Port { get; private set; } = DefaultPort;
        public LinkState State => state;
        public string LastError => lastError;

        /// <summary>The port, from env BRIDGE_PORT when set (same override as the bridge).</summary>
        public static int PortFromEnv()
        {
            string s = Environment.GetEnvironmentVariable("BRIDGE_PORT");
            return int.TryParse(s, out int p) && p > 0 && p < 65536 ? p : DefaultPort;
        }

        public void Connect(string host, int port)
        {
            Close();
            Host = host;
            Port = port;
            lastError = "";
            state = LinkState.Connecting;
            int gen = Interlocked.Increment(ref generation);
            var t = new Thread(() => Run(host, port, gen)) { IsBackground = true, Name = "BridgeClient" };
            t.Start();
        }

        public bool TryDequeue(out JObject msg)
        {
            return inbox.TryDequeue(out msg);
        }

        public bool Send(JObject msg)
        {
            if (state != LinkState.Connected) return false;
            byte[] bytes = Utf8.GetBytes(msg.ToString(Formatting.None) + "\n");
            lock (sendLock)
            {
                if (stream == null) return false;
                try
                {
                    stream.Write(bytes, 0, bytes.Length);
                    return true;
                }
                catch (Exception e)
                {
                    lastError = "Lost the bridge while sending: " + e.Message;
                    state = LinkState.Closed;
                    return false;
                }
            }
        }

        public void Close()
        {
            Interlocked.Increment(ref generation);
            lock (sendLock)
            {
                try { stream?.Close(); } catch (Exception) { }
                try { tcp?.Close(); } catch (Exception) { }
                stream = null;
                tcp = null;
            }
            if (state == LinkState.Connecting || state == LinkState.Connected) state = LinkState.Idle;
        }

        public void Dispose()
        {
            Close();
        }

        bool Current(int gen)
        {
            return Volatile.Read(ref generation) == gen;
        }

        void Run(string host, int port, int gen)
        {
            var client = new TcpClient();
            try
            {
                client.NoDelay = true;
                var task = client.ConnectAsync(host, port);
                if (!task.Wait(ConnectTimeoutMs) || !client.Connected)
                    throw new TimeoutException("no answer within " + (ConnectTimeoutMs / 1000) + " s");
            }
            catch (Exception e)
            {
                try { client.Close(); } catch (Exception) { }
                if (!Current(gen)) return;
                Exception inner = e is AggregateException ae && ae.InnerException != null ? ae.InnerException : e;
                lastError = inner.Message;
                state = LinkState.Failed;
                return;
            }

            NetworkStream ns;
            lock (sendLock)
            {
                if (!Current(gen))
                {
                    try { client.Close(); } catch (Exception) { }
                    return;
                }
                tcp = client;
                ns = client.GetStream();
                stream = ns;
                state = LinkState.Connected;
            }
            Send(Wire.Hello());

            try
            {
                using (var reader = new StreamReader(ns, Utf8, false, 1 << 16))
                {
                    while (Current(gen))
                    {
                        string line = reader.ReadLine();
                        if (line == null) break;
                        if (line.Trim().Length == 0) continue;
                        JObject obj;
                        try
                        {
                            obj = JObject.Parse(line);
                        }
                        catch (JsonException je)
                        {
                            obj = new JObject { ["type"] = "error", ["message"] = "Unreadable message from the bridge: " + je.Message };
                        }
                        inbox.Enqueue(obj);
                    }
                }
            }
            catch (Exception e)
            {
                if (Current(gen)) lastError = e.Message;
            }

            if (!Current(gen)) return;
            if (string.IsNullOrEmpty(lastError)) lastError = "The bridge closed the connection.";
            state = LinkState.Closed;
            lock (sendLock)
            {
                try { stream?.Close(); } catch (Exception) { }
                try { tcp?.Close(); } catch (Exception) { }
                stream = null;
                tcp = null;
            }
        }
    }
}
