using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Xml;
using System.Xml.Linq;

namespace Macroscop
{
    class Camera
    {
        public string Id;
        public string Name;
    }

    class Resolution
    {
        public string Name;
        public int Width;
        public int Height;
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private List<Camera> Cameras;
        private List<Resolution> Resolutions;
        private HttpWebRequest webHttp = null;
        private Thread imgThread = null;
        private delegate void OnImageDelegate(byte[] image, int count);
        private bool statusReady = false;

        public MainWindow()
        {
            InitializeComponent();
            CameraStatus.Text = "Not ready";
        }


        private void btStart_Click(object sender, RoutedEventArgs e)
        {
            if (!statusReady)
            {
                MessageBox.Show("Получите конфигурацию!");
                return;
            }

            // остановим текущую трансляцию
            btFinish_Click(sender, e);

            // соберем запрос
            string url = $"http://{CameraHost.Text}:{CameraPort.Text}/mobile?login={CameraLogin.Text}&channelid={Cameras[CameraChannel.SelectedIndex].Id}&resolutionX={Resolutions[CameraResolution.SelectedIndex].Width}&resolutionY={Resolutions[CameraResolution.SelectedIndex].Height}&fps=25";

            CameraStatus.Text = "Started";
            CameraCount.Text = "0";

            // Начать трансляцию
            webHttp = (HttpWebRequest)WebRequest.Create(url);
            webHttp.Method = "GET";
            webHttp.Timeout = 5000;
            webHttp.ReadWriteTimeout = 5000;

            imgThread = new Thread(StreamVideo);
            imgThread.IsBackground = true;
            imgThread.Start();

        }

        private void StreamVideo()
        {

            try
            {
                OnImageDelegate callback = new OnImageDelegate(OnImageReady);
                HttpWebResponse webResponse = (HttpWebResponse)webHttp.GetResponse();
                Stream streamResponse = webResponse.GetResponseStream();

                // буфер
                int maxBufferSize = 16 * 1024 * 1024;
                byte[] buffer = new byte[maxBufferSize];
                // количество кадров
                int jpegCount = 0;
                // начало кадра
                int jpegStart = -1;
                // текущая позиция в буфере, которую необходимо проанализировать
                int posInBuffer = 0;
                // позиция последнего загруженного байта в буфере
                int endInBuffer = -1;
                // сколько байт считали из потока в последний раз
                int count = 0;


                while (true)
                {
                    // читаем блок из потока в буфер
                    count = streamResponse.Read(buffer, posInBuffer, maxBufferSize - posInBuffer);

                    // если нечего не считано - завершаем декодирование
                    if (count == 0) return;

                    // posInBuffer и endInBuffer - указатели на новый блок в буфере для анализа
                    endInBuffer = posInBuffer + count - 1;

                    // ищем в буфере начало и конец кадра
                    for (; posInBuffer + 1 <= endInBuffer; posInBuffer++)
                    {
                        // смотрим на блок из 2 байт: buffer[posInBuffer], buffer[posInBuffer+1]

                        if (buffer[posInBuffer] == 0xFF && buffer[posInBuffer + 1] == 0xD8)
                        {
                            // начало кадра
                            jpegStart = posInBuffer;
                        }
                        else if (buffer[posInBuffer] == 0xFF && buffer[posInBuffer + 1] == 0xD9)
                        {
                            // конец кадра
                            if (jpegStart >=0)
                            {
                                // кадр найден, отправляем
                                int jpegSize = posInBuffer - jpegStart + 2;
                                try
                                {
                                    // копируем кадр из буфера
                                    byte[] jpeg = new byte[jpegSize];
                                    Array.Copy(buffer, jpegStart, jpeg, 0, jpegSize);
                                    jpegCount++;
                                    // вызываем функцию-делегат OnImageReady
                                    Application.Current.Dispatcher.BeginInvoke(callback, new object[] { jpeg, jpegCount });
                                }
                                catch (OverflowException)
                                {
                                    // заглушка для тестирования
                                }
                            }
                            jpegStart = -1;
                            posInBuffer++;
                        }
                    }


                    // по заверешнии анализа блока сдвигаем теущую позицию за конец анализируемого блока
                    posInBuffer = endInBuffer + 1;

                    if (posInBuffer >= maxBufferSize)
                    {
                        // если прошли по всему буферу - надо запонять буфер с начала
                        if (jpegStart > 0)
                        {
                            // буфер закончился, а начало кадра есть, надо его сохранить.
                            int ostSize = maxBufferSize - jpegStart;
                            Array.Copy(buffer, jpegStart, buffer, 0, ostSize);
                            posInBuffer = ostSize + 1;
                            jpegStart = 0;
                        }
                        else
                        {
                            // буфер закончился, начала кадра нет (jpegStart == -1) или кадр не поместился целиком в буфер (jpegStart == 0)
                            posInBuffer = 0;
                            jpegStart = -1;
                        }
                    }
                }
            }
            catch (WebException)
            {
                // выскакивает из streamResponse.Read, когда прерываю поток
            }
            catch (ThreadAbortException)
            {
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                CameraStatus.Text = "Error";
            }

        }

        private void OnImageReady(byte[] image, int count)
        {
            JpegBitmapDecoder decoder = new JpegBitmapDecoder(new MemoryStream(image), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.Default);
            BitmapSource bitmapSource = decoder.Frames[0];
            CameraImage.Source = bitmapSource;
            CameraCount.Text = count.ToString();
        }



        private void btFinish_Click(object sender, RoutedEventArgs e)
        {
            // Завершить трансляцию
            try
            {
                if (webHttp != null) webHttp.Abort();
                if (imgThread != null && imgThread.IsAlive) imgThread.Abort();
            }
            catch (ThreadAbortException)
            {
            }
            CameraStatus.Text = "Stoped";
        }

        private void btConfig_Click(object sender, RoutedEventArgs e)
        {
            statusReady = false;

            // остановим текущую трансляцию
            btFinish_Click(sender, e);

            // очистим текущий список разрешений
            Resolutions = new List<Resolution>();
            CameraResolution.Items.Clear();
            CameraResolution.SelectedIndex = -1;

            // очистим текущий список камер
            Cameras = new List<Camera>();
            CameraChannel.Items.Clear();
            CameraChannel.SelectedIndex = -1;

            // Запросим конфигурацию
            try
            {
                string url = $"http://{CameraHost.Text}:{CameraPort.Text}/configex?login={CameraLogin.Text}";
                XmlReader reader = XmlReader.Create(url);
                var elements = XElement.Load(reader).Descendants("ChannelInfo");

                // Заполним список камер, выберем первую
                foreach (var item in elements)
                {
                    Cameras.Add(new Camera() { Id = item.Attribute("Id").Value, Name = item.Attribute("Name").Value });
                    CameraChannel.Items.Add(item.Attribute("Name").Value);
                    CameraChannel.SelectedIndex = 0;
                }


                // Заполним список разрешений, выберем первое
                Resolutions.Add(new Resolution() { Name = "Low (640 x 480)", Width = 640, Height = 480 });
                Resolutions.Add(new Resolution() { Name = "Middle (800 x 480)", Width = 800, Height = 480 });
                Resolutions.Add(new Resolution() { Name = "High (1280 x 720)", Width = 1280, Height = 720 });
                foreach (var item in Resolutions)
                {
                    CameraResolution.Items.Add(item.Name);
                    CameraResolution.SelectedIndex = 0;
                }

                CameraStatus.Text = "Ready";
                statusReady = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                CameraStatus.Text = "Error";
            }


        }

        private void CameraResolution_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CameraResolution.SelectedIndex != -1)
            {
                CameraWindow.Width = Resolutions[CameraResolution.SelectedIndex].Width + ParamColumn.Width.Value;
                CameraWindow.Left = (System.Windows.SystemParameters.PrimaryScreenWidth - CameraWindow.Width) / 2;
                CameraWindow.Top = (System.Windows.SystemParameters.PrimaryScreenHeight - CameraWindow.Height) / 2;
            }
            if (statusReady) btStart_Click(sender, e);
        }
    }
}
