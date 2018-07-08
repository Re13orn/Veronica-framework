using System;
using System.Collections.Generic;
using System.Text;
using tools;
using System.Net;
using System.Net.Sockets;
using System.IO.Compression;
using System.IO;
using System.Net.Security;
using System.Text.RegularExpressions;
using System.Threading;
using System.Diagnostics;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using HTTPTool;

namespace tools
{
    public class HTTP
    {
        public const char T = '\n';
        public const String CT = "\r\n";
        public const String CTRL = "\r\n\r\n";
        public const String Content_Length_Str = "content-length: ";
        public const String Content_Length_Str_M = "Content-Length: ";
        public const String Content_Length = "content-length";
        public const String Content_Encoding = "content-encoding";
        public const String Transfer_Encoding = "transfer-encoding";
        public const String Connection = "connection";
        public static Main main = null;
        public static long index = 0;
        public void initMain(Main m)
        {
            main = m;
        }

        /**
         * 
         发生异常尝试重连  
         *
         */
        public static ServerInfo sendRequestRetry(Boolean isSSL, int tryCount, String host, int port, String payload, String request, int timeout, String encoding, Boolean foward_302)
        {
            int count = 0;
            Interlocked.Increment(ref index);
            ServerInfo server = new ServerInfo();
            timeout = timeout * 1000;
            while (true)
            {
                if (count >= tryCount) break;

                try
                {
                    if (!isSSL)
                    {
                        server = sendHTTPRequest(count, host, port, payload, request, timeout, encoding, foward_302);
                        return server;
                    }
                    else
                    {

                        server = sendHTTPSRequest(count, host, port, payload, request, timeout, encoding, foward_302);
                        return server;

                    }
                }
                catch (Exception e)
                {
                    Tools.SysLog("发包发生异常，正在重试----" + e.Message);
                    server.timeout = true;
                    continue;
                }
                finally
                {
                    count++;
                }

            }
            return server;

        }

        private static void checkContentLength(ref ServerInfo server, ref String request)
        {

            //重新计算并设置Content-length
            int sindex = request.IndexOf(CTRL);
            server.reuqestHeader = request;
            if (sindex != -1)
            {
                server.reuqestHeader = request.Substring(0, sindex);
                server.reuqestBody = request.Substring(sindex + 4, request.Length - sindex - 4);
                int contentLength = Encoding.UTF8.GetBytes(server.reuqestBody).Length;
                String newContentLength = Content_Length_Str_M + contentLength;

                if (request.IndexOf(Content_Length_Str_M) != -1)
                {
                    request = Regex.Replace(request, Content_Length_Str_M + "\\d+", newContentLength);
                }
                else
                {
                    request = request.Insert(sindex, "\r\n" + newContentLength);
                }
            }
            else
            {
                request = Regex.Replace(request, Content_Length_Str + "\\d+", Content_Length_Str_M + "0");
                request += CTRL;
            }


        }

        private static void doHeader(ref ServerInfo server, ref String[] headers)
        {

            for (int i = 0; i < headers.Length; i++)
            {
                if (i == 0)
                {

                    server.code = Tools.convertToInt(headers[i].Split(' ')[1]);

                }
                else
                {
                    String[] kv = Regex.Split(headers[i], ": ");
                    String key = kv[0].ToLower();
                    if (!server.headers.ContainsKey(key))
                    {
                        //自动识别编码
                        if ("content-type".Equals(key))
                        {
                            String hecnode = getHTMLEncoding(kv[1], "");
                            if (!String.IsNullOrEmpty(hecnode))
                            {
                                server.encoding = hecnode;
                            }
                        }
                        if (kv.Length > 1)
                        {
                            server.headers.Add(key, kv[1]);
                        }
                        else
                        {
                            server.headers.Add(key, "");
                        }
                    }
                }
            }

        }


        private static ServerInfo sendHTTPRequest(int count, String host, int port, String payload, String request, int timeout, String encoding, Boolean foward_302)
        {

            String index = Thread.CurrentThread.Name + HTTP.index;
            Stopwatch sw = new Stopwatch();
            sw.Start();
            ServerInfo server = new ServerInfo();
            TcpClient clientSocket = null;
            int sum = 0;
            try
            {
                if (port > 0 && port <= 65556)
                {
                    //编码处理
                    server.request = request;
                    TimeOutSocket tos = new TimeOutSocket();
                    clientSocket = tos.Connect(host, port, timeout);
                    if (sw.ElapsedMilliseconds >= timeout)
                    {
                        return server;
                    }
                    clientSocket.SendTimeout = timeout - tos.useTime;
                    if (clientSocket.Connected)
                    {
                        checkContentLength(ref server, ref request);
                        server.request = request;

                        byte[] requestByte = Encoding.UTF8.GetBytes(request);
                        clientSocket.Client.Send(requestByte);
                        byte[] responseBody = new byte[1024 * 1000];
                        int len = 0;
                        //获取header头
                        String tmp = "";
                        StringBuilder sb = new StringBuilder();
                        clientSocket.ReceiveTimeout = timeout - (int)sw.ElapsedMilliseconds;
                        do
                        {
                            byte[] responseHeader = new byte[1];
                            len = clientSocket.Client.Receive(responseHeader, 1, SocketFlags.None);
                            if (len == 1)
                            {

                                char c = (char)responseHeader[0];
                                sb.Append(c);
                                if (c.Equals(T))
                                {
                                    tmp = String.Concat(sb[sb.Length - 4], sb[sb.Length - 3], sb[sb.Length - 2], c);
                                }
                            }
                        } while (!tmp.Equals(CTRL) && sw.ElapsedMilliseconds < timeout);

                        server.header = sb.ToString().Replace(CTRL, "");
                        String[] headers = Regex.Split(server.header, CT);
                        if (headers != null && headers.Length > 0)
                        {
                            //处理header
                            doHeader(ref server, ref headers);
                            //自动修正编码
                            if (!String.IsNullOrEmpty(server.encoding))
                            {
                                encoding = server.encoding;
                            }
                            Encoding encod = Encoding.GetEncoding(encoding);

                            //302 301跳转
                            if ((server.code == 302 || server.code == 301) && foward_302)
                            {
                                StringBuilder rsb = new StringBuilder(server.request);
                                int urlStart = server.request.IndexOf(" ") + 1;
                                int urlEnd = server.request.IndexOf(" HTTP");
                                if (urlStart != -1 && urlEnd != -1)
                                {
                                    String url = server.request.Substring(urlStart, urlEnd - urlStart);
                                    rsb.Remove(urlStart, url.Length);
                                    String location = server.headers["location"];
                                    if (!server.headers["location"].StartsWith("/") && !server.headers["location"].StartsWith("http"))
                                    {
                                        location = Tools.getCurrentPath(url) + location;
                                    }
                                    rsb.Insert(urlStart, location);

                                    return sendHTTPRequest(count, host, port, payload, rsb.ToString(), timeout, encoding, false);
                                }

                            }


                            //根据请求头解析
                            if (server.headers.ContainsKey(Content_Length))
                            {
                                int length = int.Parse(server.headers[Content_Length]);

                                while (sum < length && sw.ElapsedMilliseconds < timeout)
                                {
                                    int readsize = length - sum;
                                    len = clientSocket.Client.Receive(responseBody, sum, readsize, SocketFlags.None);
                                    if (len > 0)
                                    {
                                        sum += len;
                                    }
                                }
                            }
                            //解析chunked传输
                            else if (server.headers.ContainsKey(Transfer_Encoding))
                            {
                                //读取长度
                                int chunkedSize = 0;
                                byte[] chunkedByte = new byte[1];
                                //读取总长度
                                sum = 0;
                                do
                                {
                                    String ctmp = "";
                                    do
                                    {
                                        len = clientSocket.Client.Receive(chunkedByte, 1, SocketFlags.None);
                                        ctmp += Encoding.UTF8.GetString(chunkedByte);

                                    } while ((ctmp.IndexOf(CT) == -1) && (sw.ElapsedMilliseconds < timeout));

                                    chunkedSize = Tools.convertToIntBy16(ctmp.Replace(CT, ""));

                                    //chunked的结束0\r\n\r\n是结束标志，单个chunked块\r\n结束
                                    if (ctmp.Equals(CT))
                                    {
                                        continue;
                                    }
                                    if (chunkedSize == 0)
                                    {
                                        //结束了
                                        break;
                                    }
                                    int onechunkLen = 0;
                                    while (onechunkLen < chunkedSize && sw.ElapsedMilliseconds < timeout)
                                    {
                                        len = clientSocket.Client.Receive(responseBody, sum, chunkedSize - onechunkLen, SocketFlags.None);
                                        if (len > 0)
                                        {
                                            onechunkLen += len;
                                            sum += len;
                                        }
                                    }

                                    //判断
                                } while (sw.ElapsedMilliseconds < timeout);
                            }
                            //connection close方式或未知body长度
                            else
                            {
                                while (sw.ElapsedMilliseconds < timeout)
                                {
                                    if (clientSocket.Client.Poll(timeout, SelectMode.SelectRead))
                                    {
                                        if (clientSocket.Available > 0)
                                        {
                                            len = clientSocket.Client.Receive(responseBody, sum, (1024 * 200) - sum, SocketFlags.None);
                                            if (len > 0)
                                            {
                                                sum += len;
                                            }
                                        }
                                        else
                                        {
                                            break;
                                        }
                                    }
                                }
                            }
                            //判断是否gzip
                            if (server.headers.ContainsKey(Content_Encoding))
                            {
                                server.body = unGzip(responseBody, sum, encod);
                            }
                            else
                            {
                                server.body = encod.GetString(responseBody, 0, sum);
                            }


                        }
                    }

                }
            }
            catch (Exception e)
            {
                Exception ee = new Exception("HTTP发包错误！错误消息：" + e.Message + e.TargetSite.Name + "----发包编号：" + index);
                throw ee;
            }
            finally
            {
                sw.Stop();
                server.length = sum;
                server.runTime = (int)sw.ElapsedMilliseconds;
                if (clientSocket != null)
                {
                    clientSocket.Close();
                }
            }
            return server;

        }

        private static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }
        private static ServerInfo sendHTTPSRequest(int count, String host, int port, String payload, String request, int timeout, String encoding, Boolean foward_302)
        {
            String index = Thread.CurrentThread.Name + HTTP.index;
            Stopwatch sw = new Stopwatch();
            sw.Start();
            ServerInfo server = new ServerInfo();

            int sum = 0;

            TcpClient clientSocket = null; ;

            try
            {

                if (port > 0 && port <= 65556)
                {

                    TimeOutSocket tos = new TimeOutSocket();
                    clientSocket = tos.Connect(host, port, timeout);
                    if (sw.ElapsedMilliseconds >= timeout)
                    {
                        return server;
                    }
                    clientSocket.SendTimeout = timeout - tos.useTime;

                    SslStream ssl = null;
                    if (clientSocket.Connected)
                    {
                        ssl = new SslStream(clientSocket.GetStream(), false, new RemoteCertificateValidationCallback(ValidateServerCertificate));
                        SslProtocols protocol = SslProtocols.Ssl3 | SslProtocols.Ssl2 | SslProtocols.Tls;
                        ssl.AuthenticateAsClient(host, null, protocol, false);
                        if (ssl.IsAuthenticated)
                        {
                            checkContentLength(ref server, ref request);
                            server.request = request;
                            byte[] requestByte = Encoding.UTF8.GetBytes(request);
                            ssl.Write(requestByte);
                            ssl.Flush();
                        }
                    }
                    server.request = request;
                    byte[] responseBody = new byte[1024 * 1000];
                    int len = 0;
                    //获取header头
                    String tmp = "";

                    StringBuilder sb = new StringBuilder();
                    StringBuilder bulider = new StringBuilder();
                    clientSocket.ReceiveTimeout = timeout - (int)sw.ElapsedMilliseconds;
                    do
                    {
                        byte[] responseHeader = new byte[1];
                        int read = ssl.ReadByte();

                        char c = (char)read;
                        sb.Append(c);
                        if (c.Equals(T))
                        {
                            tmp = String.Concat(sb[sb.Length - 4], sb[sb.Length - 3], sb[sb.Length - 2], c);
                        }

                    } while (!tmp.Equals(CTRL) && sw.ElapsedMilliseconds < timeout);

                    server.header = sb.ToString().Replace(CTRL, "");
                    String[] headers = Regex.Split(server.header, CT);
                    //处理header
                    doHeader(ref server, ref headers);
                    //自动修正编码
                    if (!String.IsNullOrEmpty(server.encoding))
                    {
                        encoding = server.encoding;
                    }
                    Encoding encod = Encoding.GetEncoding(encoding);
                    //302 301跳转
                    if ((server.code == 302 || server.code == 301) && foward_302)
                    {

                        int urlStart = server.request.IndexOf(" ");
                        int urlEnd = server.request.IndexOf(" HTTP");
                        if (urlStart != -1 && urlEnd != -1)
                        {
                            String url = server.request.Substring(urlStart + 1, urlEnd - urlStart - 1);
                            if (!server.headers["location"].StartsWith("/") && !server.headers["location"].StartsWith("https"))
                            {
                                server.request = server.request.Replace(url, Tools.getCurrentPath(url) + server.headers["location"]);
                            }
                            else
                            {
                                server.request = server.request.Replace(url, server.headers["location"]);
                            }

                            return sendHTTPSRequest(count, host, port, payload, server.request, timeout, encoding, false);
                        }

                    }


                    //根据请求头解析
                    if (server.headers.ContainsKey(Content_Length))
                    {
                        int length = int.Parse(server.headers[Content_Length]);
                        while (sum < length && sw.ElapsedMilliseconds < timeout)
                        {
                            len = ssl.Read(responseBody, sum, length - sum);
                            if (len > 0)
                            {
                                sum += len;
                            }
                        }
                    }
                    //解析chunked传输
                    else if (server.headers.ContainsKey(Transfer_Encoding))
                    {
                        //读取长度
                        int chunkedSize = 0;
                        byte[] chunkedByte = new byte[1];
                        //读取总长度
                        sum = 0;
                        do
                        {
                            String ctmp = "";
                            do
                            {
                                len = ssl.Read(chunkedByte, 0, 1);
                                ctmp += Encoding.UTF8.GetString(chunkedByte);

                            } while (ctmp.IndexOf(CT) == -1 && sw.ElapsedMilliseconds < timeout);

                            chunkedSize = Tools.convertToIntBy16(ctmp.Replace(CT, ""));

                            //chunked的结束0\r\n\r\n是结束标志，单个chunked块\r\n结束
                            if (ctmp.Equals(CT))
                            {
                                continue;
                            }
                            if (chunkedSize == 0)
                            {
                                //结束了
                                break;
                            }
                            int onechunkLen = 0;

                            while (onechunkLen < chunkedSize && sw.ElapsedMilliseconds < timeout)
                            {
                                len = ssl.Read(responseBody, sum, chunkedSize - onechunkLen);
                                if (len > 0)
                                {
                                    onechunkLen += len;
                                    sum += len;
                                }
                            }

                            //判断
                        } while (sw.ElapsedMilliseconds < timeout);
                    }
                    //connection close方式或未知body长度
                    else
                    {
                        while (sw.ElapsedMilliseconds < timeout)
                        {
                            if (clientSocket.Client.Poll(timeout, SelectMode.SelectRead))
                            {
                                if (clientSocket.Available > 0)
                                {
                                    len = ssl.Read(responseBody, sum, (1024 * 200) - sum);
                                    if (len > 0)
                                    {
                                        sum += len;
                                    }
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                    }
                    //判断是否gzip
                    if (server.headers.ContainsKey(Content_Encoding))
                    {
                        server.body = unGzip(responseBody, sum, encod);
                    }
                    else
                    {
                        server.body = encod.GetString(responseBody, 0, sum);
                    }
                }

            }
            catch (Exception e)
            {
                Exception ee = new Exception("HTTPS发包错误！错误消息：" + e.Message + "----发包编号：" + index);
                throw ee;
            }
            finally
            {
                sw.Stop();
                server.length = sum;
                server.runTime = (int)sw.ElapsedMilliseconds;

                if (clientSocket != null)
                {
                    clientSocket.Close();
                }
            }
            return server;

        }

        public static String unGzip(byte[] data, int len, Encoding encoding)
        {

            String str = "";
            MemoryStream ms = new MemoryStream(data, 0, len);
            GZipStream gs = new GZipStream(ms, CompressionMode.Decompress);
            MemoryStream outbuf = new MemoryStream();
            byte[] block = new byte[1024];

            try
            {

                while (true)
                {
                    int bytesRead = gs.Read(block, 0, block.Length);
                    if (bytesRead <= 0)
                    {
                        break;
                    }
                    else
                    {
                        outbuf.Write(block, 0, bytesRead);
                    }
                }
                str = encoding.GetString(outbuf.ToArray());
            }
            catch (Exception e)
            {
                Tools.SysLog("解压Gzip发生异常----" + e.Message);
            }
            finally
            {
                outbuf.Close();
                gs.Close();
                ms.Close();

            }
            return str;

        }
        public static String getHTMLEncoding(String header, String body)
        {
            if (String.IsNullOrEmpty(header) && String.IsNullOrEmpty(body))
            {
                return "";
            }
            body = body.ToUpper();
            Match m = Regex.Match(header, @"charset\b\s*=\s*""?(?<charset>[^""]*)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                return m.Groups["charset"].Value.ToUpper();
            }
            else
            {
                if (String.IsNullOrEmpty(body))
                {
                    return "";
                }
                m = Regex.Match(body, @"charset\b\s*=\s*""?(?<charset>[^""]*)", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    return m.Groups["charset"].Value.ToUpper();
                }
            }
            return "";
        }
    }
}