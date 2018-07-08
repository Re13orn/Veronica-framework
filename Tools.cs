using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Net.Sockets;
using System.Threading;
using tools;
using System.Globalization;
using System.Security.Cryptography;


namespace tools
{
    class Tools
    {
        public const String httpLogPath = "logs/http/";

        public static long currentMillis()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
        }

        public static void SysLog(String log)
        {
            AppendLogToFile("logs/" + DateTime.Now.ToLongDateString() + ".log.txt", log + "----" + DateTime.Now);
        }
        /// <summary>
        /// 将16进制转换成10进制
        /// </summary>
        /// <param name="str">16进制字符串</param>
        /// <returns></returns>
        public static int convertToIntBy16(String str)
        {
            try
            {
                return Convert.ToInt32(str, 16);
            }
            catch (Exception e)
            {

            }
            return 0;

        }
        public static String getCurrentPath(String url)
        {
            int index = url.LastIndexOf("/");

            if (index != -1)
            {
                return url.Substring(0, index) + "/";
            }
            else
            {
                return "";
            }
        }


        public static object c = "";
        public static void AppendLogToFile(String path, String log)
        {
            //锁住，防止多线程引发错误
            lock (c)
            {
                List<String> list = new List<String>();
                FileStream fs_dir = null;
                StreamWriter sw = null;
                try
                {
                    fs_dir = new FileStream(AppDomain.CurrentDomain.BaseDirectory + "/" + path, FileMode.Append, FileAccess.Write);

                    sw = new StreamWriter(fs_dir);

                    sw.WriteLine(log);

                    sw.Close();

                    fs_dir.Close();

                }
                catch (Exception e)
                {
                    Tools.SysLog("FileTools-AppendLogToFile-error:读取文件内容发生错误！" + e.Message);
                }
                finally
                {
                    if (sw != null)
                    {
                        sw.Close();
                    }
                    if (fs_dir != null)
                    {
                        fs_dir.Close();
                    }
                }
            }

        }

        /// <summary>
        /// 将字符串转换成数字，错误返回0
        /// </summary>
        /// <param name="strs">字符串</param>
        /// <returns></returns>
        public static int convertToInt(String str)
        {

            try
            {
                return int.Parse(str);
            }
            catch (Exception e) {
                Tools.SysLog("info:-"+e.Message);
            }
            return 0;

        }
     
    }
}
