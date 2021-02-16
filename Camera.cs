using System;
using System.Xml;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Text;

namespace TestVideoClient
{
    public delegate void ImageReadyHandler(Bitmap image);
    public delegate void ErrorHandler(string message);

    class Camera
    {
        const int blockSize = 5000;
        const string boundary = "--myboundary";
        const string hostUrl = "http://demo.macroscop.com:8080/";


        public event ImageReadyHandler ImageReady;
        private void OnImageReady(Bitmap image)
        {
            ImageReady?.Invoke(image);
        }

        public static event ErrorHandler Error;
        private static void OnError(string message)
        {
            Error?.Invoke(message);
        }


        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public string DeviceInfo { get; private set; }
        public string AttachedToServer { get; private set; }
        public bool IsDisabled { get; private set; }
        public bool IsSoundOn { get; private set; }
        public bool IsArchivingEnabled { get; private set; }
        public bool IsSoundArchivingEnabled { get; private set; }
        public bool AllowedRealtime { get; private set; }
        public bool AllowedArchive { get; private set; }
        public bool IsTransmitSoundOn { get; private set; }
        public string ArchiveMode { get; private set; }
        public string ArchiveStreamType { get; private set; }
        public bool IsFaceAnalystEnabled { get; private set; }

        private CancellationTokenSource tokenSource;


        public Camera(XmlNode node)
        {
            XmlAttributeCollection test = node.Attributes;
            Id =                        test.GetNamedItem("Id").Value;
            Name =                      test.GetNamedItem("Name").Value;
            Description =               test.GetNamedItem("Description").Value;
            DeviceInfo =                test.GetNamedItem("DeviceInfo").Value;
            AttachedToServer =          test.GetNamedItem("AttachedToServer").Value;
            IsDisabled =                Convert.ToBoolean(test.GetNamedItem("IsDisabled").Value);
            IsSoundOn =                 Convert.ToBoolean(test.GetNamedItem("IsSoundOn").Value);
            IsArchivingEnabled =        Convert.ToBoolean(test.GetNamedItem("IsArchivingEnabled").Value);
            IsSoundArchivingEnabled =   Convert.ToBoolean(test.GetNamedItem("IsSoundArchivingEnabled").Value);
            AllowedRealtime =           Convert.ToBoolean(test.GetNamedItem("AllowedRealtime").Value);
            AllowedArchive =            Convert.ToBoolean(test.GetNamedItem("AllowedArchive").Value);
            IsTransmitSoundOn =         Convert.ToBoolean(test.GetNamedItem("IsTransmitSoundOn").Value);
            ArchiveMode =               test.GetNamedItem("ArchiveMode").Value;
            ArchiveStreamType =         test.GetNamedItem("ArchiveStreamType").Value;
            IsFaceAnalystEnabled =      Convert.ToBoolean(test.GetNamedItem("IsFaceAnalystEnabled").Value);
        }

        public string HttpRequestUrl(int resolutionX = 640, int resolutionY = 480, int fps = 25)
        {
            return hostUrl + $"mobile?login=root&channelid={Id}&resolutionX={resolutionX}&resolutionY={resolutionY}&fps={fps}";
        }

        public void StartStream(int resolutionX = 640, int resolutionY = 480, int fps = 25)
        {
            StopStream();

            tokenSource = new CancellationTokenSource();
            var token = tokenSource.Token;

            Task.Run(() => GetCameraStream(token, resolutionX, resolutionY, fps));
        }

        public void StopStream()
        {
            if (tokenSource != null)
            {
                tokenSource.Cancel();
            }
        }

        private void GetCameraStream(CancellationToken token, int resolutionX = 640, int resolutionY = 480, int fps = 25)
        {
            WebRequest request = WebRequest.Create(HttpRequestUrl(resolutionX, resolutionY, fps));
            request.Timeout = 10000;
            WebResponse response;
            try
            {
                response = request.GetResponse();
            }
            catch (WebException e)
            {
                OnError(e.Message);
                return;
            }

            using (Stream stream = response.GetResponseStream())
            {
                byte[] streamBytes = new byte[1024 * 1024];
                int size = 0;
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            byte[] chunk = reader.ReadBytes(blockSize);
                            Array.Copy(chunk, 0, streamBytes, size, chunk.Length);
                            size += chunk.Length;

                            int segmentStart = FindPicture(streamBytes, size);
                            Array.Copy(streamBytes, segmentStart, streamBytes, 0, size - segmentStart);
                            size -= segmentStart;
                        }
                        catch (IOException e)
                        {
                            OnError(e.Message);
                            break;
                        }
                    }
                }
            }
            response.Close();
        }

        private int FindPicture(byte[] streamBytes, int size)
        {
            byte[] boundaryBytes = Encoding.UTF8.GetBytes(boundary);
            int segmentStart = IndexOf(streamBytes, boundaryBytes, 0, size),
                segmentEnd = IndexOf(streamBytes, boundaryBytes, segmentStart + 1, size);

            if (segmentEnd == -1)
                return segmentStart > 0 ? segmentStart : 0;

            byte[] separator = new byte[] { 0x0d, 0x0a, 0x0d, 0x0a };       // \r\n\r\n
            int pictureStart = IndexOf(streamBytes, separator, segmentStart) + 4;

            byte[] pictureBytes = new byte[segmentEnd - pictureStart];
            Array.Copy(streamBytes, pictureStart, pictureBytes, 0, segmentEnd - pictureStart);

            Bitmap image;
            using (var ms = new MemoryStream(pictureBytes))
            {
                image = new Bitmap(ms);
            }

            OnImageReady(image);

            return segmentEnd;
        }


        public static async Task<List<Camera>> GetCameraList()
        {
            string xmlString = await GetCameraListXml();
            return ParseCameraListXml(xmlString);
        }

        private static async Task<string> GetCameraListXml()
        {
            string text;
            WebRequest request = WebRequest.Create(hostUrl + "configex?login=root");
            request.Timeout = 10000;
            WebResponse response = await request.GetResponseAsync();
            using (Stream stream = response.GetResponseStream())
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    text = reader.ReadToEnd();
                }
            }
            response.Close();

            return text;
        }

        private static List<Camera> ParseCameraListXml(string text)
        {
            List<Camera> cameras = new List<Camera>();

            XmlDocument xDoc = new XmlDocument();
            xDoc.LoadXml(text);
            XmlElement root = xDoc.DocumentElement;
            var channels = root.SelectSingleNode("Channels");
            foreach (XmlNode item in channels.ChildNodes)
            {
                Camera camera = new Camera(item);
                cameras.Add(camera);
            }
            return cameras;
        }

        private static int IndexOf(byte[] array, byte[] target, int start = 0, int end = -1)
        {
            end = end == -1 ? array.Length : end;
            for (; start < end - target.Length; start++)
            {
                if (array[start] == target[0])
                {
                    bool find = true;
                    for (int next = 1; next < target.Length; next++)
                    {
                        find = array[start + next] == target[next];
                        if (!find)
                            break;
                    }

                    if (find)
                        return start;
                }
            }
            return -1;
        }
    }
}
