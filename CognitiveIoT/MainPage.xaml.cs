using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using Windows.ApplicationModel.Core;
using Windows.UI.Core;

using Windows.Storage;
using Windows.Media.Capture;

using System.Threading.Tasks;

using System.Diagnostics;

using Windows.Media.MediaProperties;
using Windows.UI.Xaml.Media.Imaging;

using Windows.Media.SpeechSynthesis;

using Microsoft.ProjectOxford.Vision;
using Microsoft.ProjectOxford.Vision.Contract;
using Emmellsoft.IoT.Rpi.SenseHat;
using System.Threading;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace CognitiveIoT
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        ISenseHat SenseHat;
        private DispatcherTimer _timer;
        public MediaCapture mediaCapture;
        public VideoEncodingProperties maxRes;
        private StorageFile photoFile;
        private readonly string PHOTO_FILE_NAME = "img.jpg";
        public bool isProcessing = false;

        private AnalysisResult analysisResult;
        private SpeechSynthesisStream stream;
        private MediaElement mediaElement;

        private readonly ManualResetEventSlim _waitEvent = new ManualResetEventSlim(false);

        public MainPage()
        {
            this.InitializeComponent();
            this.Loaded += MainPage_Loaded;
        }

        protected void Sleep(TimeSpan duration)
        {
            _waitEvent.Wait(duration);
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            //get a reference to SenseHat
            SenseHat = await SenseHatFactory.GetSenseHat();

            SenseHat.Display.Clear();
            SenseHat.Display.Update();

            _timer = new DispatcherTimer();
            _timer.Interval = new TimeSpan(0, 0, 0, 0, 1);
            CameraOn();

            _timer.Start();
            _timer.Tick += _timer_Tick;
        }

        private async void _timer_Tick(object sender, object e)
        {
            try
            {
                //Debug.WriteLine("Timer tick");
                if (SenseHat.Joystick.Update()) // Has any of the buttons on the joystick changed?
                {

                    if ((!isProcessing)&&((SenseHat.Joystick.EnterKey == KeyState.Pressed)|| (SenseHat.Joystick.EnterKey == KeyState.Pressing)))
                    {
                        isProcessing = true;
                        Debug.WriteLine("Button Pressed");
                        //Speak("Click Click");
                        TakePhoto();
                    }
                    else if (SenseHat.Joystick.LeftKey == KeyState.Pressed)
                    {
                        // unblock photo processing if it gets locked waiting for Azure to respond.
                        isProcessing = false;
                    }
                }

                if ((SenseHat.Display.Screen[0, 0].ToString() == "#FF000000") && (isProcessing))
                {
                    FillDisplayBlocks();
                    SenseHat.Display.Update();
                }
                else if ((SenseHat.Display.Screen[0, 0].ToString() == "#FF00FF00") && (!isProcessing))
                {
                    SenseHat.Display.Clear();
                    SenseHat.Display.Update();
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            finally
            {

            }
        }

        public async void TakePhoto()
        {
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
            () =>
            {
                _TakePhoto();
            }
            );
        }

        private async void _TakePhoto()
        {
            Debug.WriteLine("Taking Photo");

            try
            {
                photoFile = await KnownFolders.PicturesLibrary.CreateFileAsync(PHOTO_FILE_NAME, CreationCollisionOption.ReplaceExisting);
                var imageProperties = ImageEncodingProperties.CreateJpeg();

                await mediaCapture.CapturePhotoToStorageFileAsync(imageProperties, photoFile);

                var photoStream = await photoFile.OpenReadAsync();
                var bitmap = new BitmapImage();
                bitmap.SetSource(photoStream);
                captureImage.Source = bitmap;

                using (Stream imageFileStream = (await photoFile.OpenReadAsync()).AsStreamForRead())
                {
                    // Analyze the image for all visual features
                    Debug.WriteLine("Calling VisionServiceClient.AnalyzeImageAsync()...");
                    VisualFeature[] visualFeatures = new VisualFeature[]
                    {
                    VisualFeature.Adult, VisualFeature.Categories, VisualFeature.Color, VisualFeature.Description,
                    VisualFeature.Faces, VisualFeature.ImageType, VisualFeature.Tags
                    };

                    // add your API key in here
                    VisionServiceClient VisionServiceClient = new VisionServiceClient("");

                    analysisResult =
                        await VisionServiceClient.AnalyzeImageAsync(imageFileStream, visualFeatures);

                    Debug.WriteLine("photo: " + analysisResult.Description.Captions[0].Text + " , " + analysisResult.Description.Captions[0].Confidence);

                    if (analysisResult.Description.Captions[0].Confidence > 0.6d)
                    {
                        Speak("I see, " + analysisResult.Description.Captions[0].Text);
                    }
                    else
                    {
                        Speak("I'm not quite sure but it could be, " + analysisResult.Description.Captions[0].Text);
                    }
                    
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
 
            }
            finally
            {
                isProcessing = false;
            }
  
        }

        private void FillDisplayBlocks()
        {
            Windows.UI.Color pixel = Windows.UI.Color.FromArgb(
                        255,
                        (byte)(0),
                        (byte)(255),
                        (byte)(0));

            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    SenseHat.Display.Screen[x, y] = pixel;
                }
            }
        }

        private async void CameraOn()
        {
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
            () =>
            {
                _CameraOn();
            }
            );

        }

        private async void _CameraOn()
        {

            //Video and Audio is initialized by default  
            mediaCapture = new MediaCapture();
            await mediaCapture.InitializeAsync();

            // Start Preview                  
            PreviewElement.Source = mediaCapture;
            await mediaCapture.StartPreviewAsync();

            var resolutions = mediaCapture.VideoDeviceController.GetAvailableMediaStreamProperties(MediaStreamType.Photo).Select(x => x as VideoEncodingProperties);
            maxRes = resolutions.OrderByDescending(x => x.Height * x.Width).FirstOrDefault();
        }


        // Speak the text
        public async void Speak(string text)
        {
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.High,
            () =>
            {
                _Speak(text);
            }
            );
        }

        // Internal speak method
        private async void _Speak(string text)
        {

            SpeechSynthesizer synth = new SpeechSynthesizer()
            {
                Voice = SpeechSynthesizer.AllVoices[1]
            };

            stream = await synth.SynthesizeTextToStreamAsync(text);


            // Send the stream to the media object.
            mediaElement = new MediaElement();
            mediaElement.SetSource(stream, stream.ContentType);
            mediaElement.Play();
            Sleep(TimeSpan.FromMilliseconds(1000));
            mediaElement.Stop();
            synth.Dispose();
        }


    }
}
