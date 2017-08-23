// 
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license.
// 
// Microsoft Cognitive Services: http://www.microsoft.com/cognitive
// 
// Microsoft Cognitive Services Github:
// https://github.com/Microsoft/Cognitive
// 
// Copyright (c) Microsoft Corporation
// All rights reserved.
// 
// MIT License:
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// 

using IntelligentKioskSample.Controls;
using Microsoft.ProjectOxford.Common.Contract;
using Microsoft.ProjectOxford.Face.Contract;
using Newtonsoft.Json;

using ServiceHelpers;
using System;
using System.Diagnostics;
using System.Text;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Windows.Graphics.Imaging;
using Windows.UI.Popups;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Media;
using Windows.Media.SpeechRecognition;
using Windows.Globalization;
using Windows.Foundation;
using WeatherAssignment;

#region using for Text to Speech
using System.IO;
using Windows.Media;
using System.Threading;
using CognitiveServicesTTS;
using Windows.UI.Core;
using Windows.Media.SpeechSynthesis;
using Windows.ApplicationModel.Resources.Core;
using Windows.ApplicationModel.Core;
#endregion

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace IntelligentKioskSample.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    [KioskExperience(Title = "即時群眾人臉分析", ImagePath = "ms-appx:/Assets/realtime.png", ExperienceType = ExperienceType.Kiosk)]
    public sealed partial class RealTimeDemo : Page, IRealTimeDataProvider
    {
        private Task processingLoopTask;
        private bool isProcessingLoopInProgress;
        private bool isProcessingPhoto;

        private IEnumerable<Emotion> lastEmotionSample;
        private IEnumerable<Face> lastDetectedFaceSample;
        private IEnumerable<Tuple<Face, IdentifiedPerson>> lastIdentifiedPersonSample;
        private IEnumerable<SimilarFaceMatch> lastSimilarPersistedFaceSample;

        private DemographicsData demographics;
        private Dictionary<Guid, Visitor> visitors = new Dictionary<Guid, Visitor>();

        private static uint HResultPrivacyStatementDeclined = 0x80045509;
        private static uint HResultRecognizerNotFound = 0x8004503a;

        private int starving_count;
        private int last_latency;
        private int cur_latency;
        private static string deviceName;

        bool GreetVisitor;

        private SpeechSynthesizer synthesizer;

        Authentication auth = new Authentication("475623a6b9fc456d904015983b13ba40");
        Synthesize cortana = new Synthesize();

        private SpeechRecognizer speechRecognizer;
        private CoreDispatcher dispatcher;
        private ResourceContext speechContext;
        private ResourceMap speechResourceMap;
        private IAsyncOperation<SpeechRecognitionResult> recognitionOperation;

        public static string DeviceName
        {
            get { return deviceName; }
            set
            {
                deviceName = value;
            }
        }

        public RealTimeDemo()
        {
            this.InitializeComponent();
            this.DataContext = this;

            Window.Current.Activated += CurrentWindowActivationStateChanged;
            this.saveControl.SetRealTimeDataProvider(this);
            this.saveControl.FilterOutSmallFaces = true;
            //this.cameraControl.HideCameraControls();
            this.saveControl.CameraAspectRatioChanged += CameraControl_CameraAspectRatioChanged;
            synthesizer = new SpeechSynthesizer();
            starving_count = 0;
            last_latency = -1;
            cur_latency = -2;
            GreetVisitor = false;
        }

        private async void initMicrophone()
        {
            Microphone.IsEnabled = false;
            if (speechRecognizer != null)
            {
                if (speechRecognizer.State != SpeechRecognizerState.Idle)
                {
                    if (recognitionOperation != null)
                    {
                        recognitionOperation.Cancel();
                        recognitionOperation = null;
                    }
                }

                speechRecognizer.StateChanged -= SpeechRecognizer_StateChanged;

                this.speechRecognizer.Dispose();
                this.speechRecognizer = null;
            }
            try
            {
                Language chiCN = new Language("zh-CN");
                await InitializeRecognizer(chiCN);
                Microphone.IsEnabled = true;
            }
            catch (Exception exception)
            {
                var messageDialog = new Windows.UI.Popups.MessageDialog(exception.Message, "Exception");
                await messageDialog.ShowAsync();
                Microphone.IsEnabled = false;
            }
        }

        private async Task InitializeRecognizer(Language recognizerLanguage)
        {
            if (speechRecognizer != null)
            {
                // cleanup prior to re-initializing this scenario.
                speechRecognizer.StateChanged -= SpeechRecognizer_StateChanged;

                this.speechRecognizer.Dispose();
                this.speechRecognizer = null;
            }
            try
            {
                // Create an instance of SpeechRecognizer.
                speechRecognizer = new SpeechRecognizer(recognizerLanguage);

                // Provide feedback to the user about the state of the recognizer.
                speechRecognizer.StateChanged += SpeechRecognizer_StateChanged;
                //Add a web search topic constraint to the recognizer.
                var webSearchGrammar = new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.WebSearch, "webSearch");
                speechRecognizer.Constraints.Add(webSearchGrammar);
                // Compile the constraint.
                SpeechRecognitionCompilationResult compilationResult = await speechRecognizer.CompileConstraintsAsync();
                if (compilationResult.Status != SpeechRecognitionResultStatus.Success)
                {
                    Microphone.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                if ((uint)ex.HResult == HResultRecognizerNotFound)
                {
                    var messageDialog = new Windows.UI.Popups.MessageDialog("Speech Language pack for selected language not installed.");
                    await messageDialog.ShowAsync();
                }
                else
                {
                    var messageDialog = new Windows.UI.Popups.MessageDialog(ex.Message, "Exception");
                    await messageDialog.ShowAsync();
                }
            }
        }

        private async void SpeechRecognizer_StateChanged(SpeechRecognizer sender, SpeechRecognizerStateChangedEventArgs args)
        {
            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                Debug.WriteLine("Speech recognizer state: " + args.State.ToString());
            });
        }

        private void CameraControl_CameraAspectRatioChanged(object sender, EventArgs e)
        {
            this.UpdateCameraHostSize();
        }

        private void StartProcessingLoop()
        {
            this.isProcessingLoopInProgress = true;

            if (this.processingLoopTask == null || this.processingLoopTask.Status != TaskStatus.Running)
            {
                this.processingLoopTask = Task.Run(() => this.ProcessingLoop());
            }
        }

        private async void ProcessingLoop()
        {
            while (this.isProcessingLoopInProgress)
            {
                await this.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, async () =>
                {
                    if (!this.isProcessingPhoto)
                    {
                        if (DateTime.Now.Hour != this.demographics.StartTime.Hour)
                        {
                            // We have been running through the hour. Reset the data...
                            await this.ResetDemographicsData();
                            this.UpdateDemographicsUI();
                        }

                        this.isProcessingPhoto = true;
                        last_latency = cur_latency;
                        this.starving_count = 0;
                        if (this.saveControl.NumFacesOnLastFrame == 0)
                        {
                            await this.ProcessCameraCapture(null);
                        }
                        else
                        {
                            try
                            {
                                await this.ProcessCameraCapture(await this.saveControl.CaptureFrameAsync());
                            }
                            catch(NullReferenceException e)
                            {
                                //ignore 
                                //await new MessageDialog("Error.", "Missing API Key").ShowAsync();
                                if (e.Source != null)
                                    this.debugText.Text = string.Format("NullRefenrenceException source: {0}", e.Source);
                            }
                        }
                    }
                    else
                    {
                        if (this.last_latency == this.cur_latency)
                        {
                            this.starving_count++;
                        }
                        if(this.starving_count > 7)
                        {
                            cur_latency = -1;
                            this.isProcessingPhoto = false;

                        }
                    }
                });

                await Task.Delay(2000);
            }
        }

        private async void CurrentWindowActivationStateChanged(object sender, Windows.UI.Core.WindowActivatedEventArgs e)
        {
            if ((e.WindowActivationState == Windows.UI.Core.CoreWindowActivationState.CodeActivated ||
                e.WindowActivationState == Windows.UI.Core.CoreWindowActivationState.PointerActivated) &&
                this.saveControl.CameraStreamState == Windows.Media.Devices.CameraStreamState.Shutdown)
            {
                // When our Window loses focus due to user interaction Windows shuts it down, so we 
                // detect here when the window regains focus and trigger a restart of the camera.
                await this.saveControl.StartStreamAsync(isForRealTimeProcessing: true);
            }
        }

        private async Task ProcessCameraCapture(ImageAnalyzer e)
        {
            if (e == null)
            {
                this.lastDetectedFaceSample = null;
                this.lastIdentifiedPersonSample = null;
                this.lastSimilarPersistedFaceSample = null;
                this.lastEmotionSample = null;
                this.debugText.Text = "";
                UpdateUIForNoFacesDetected();
                this.helpButton.Visibility = Visibility.Collapsed;
                this.isProcessingPhoto = false;
                return;
            }

            DateTime start = DateTime.Now;

            // Compute Emotion, Age and Gender
            await Task.WhenAll(e.DetectEmotionAsync(), e.DetectFacesAsync(detectFaceAttributes: true));

            if (!e.DetectedEmotion.Any())
            {
                this.lastEmotionSample = null;
                this.ShowTimelineFeedbackForNoFaces();
            }
            else
            {

                this.lastEmotionSample = e.DetectedEmotion;

                EmotionScores averageScores = new EmotionScores
                {
                    Happiness = e.DetectedEmotion.Average(em => em.Scores.Happiness),
                    Anger = e.DetectedEmotion.Average(em => em.Scores.Anger),
                    Sadness = e.DetectedEmotion.Average(em => em.Scores.Sadness),
                    Contempt = e.DetectedEmotion.Average(em => em.Scores.Contempt),
                    Disgust = e.DetectedEmotion.Average(em => em.Scores.Disgust),
                    Neutral = e.DetectedEmotion.Average(em => em.Scores.Neutral),
                    Fear = e.DetectedEmotion.Average(em => em.Scores.Fear),
                    Surprise = e.DetectedEmotion.Average(em => em.Scores.Surprise)
                };

                this.emotionDataTimelineControl.DrawEmotionData(averageScores);
            }



            if (e.DetectedFaces == null || !e.DetectedFaces.Any())
            {
                UpdateUIForNoFacesDetected();
                this.lastDetectedFaceSample = null;
            }
            else
            {
                this.lastDetectedFaceSample = e.DetectedFaces;
                await e.IdentifyFacesAsync();
                //this.greetingTextBlock.Text = this.GetGreettingFromFaces(e);
            }

            // Compute Face Identification and Unique Face Ids
            await Task.WhenAll(e.IdentifyFacesAsync(), e.FindSimilarPersistedFacesAsync());

            

            if (!e.IdentifiedPersons.Any())
            {
                this.helpButton.Visibility = Visibility.Visible;
                this.lastIdentifiedPersonSample = null;
            }
            else
            {
                if (e.IdentifiedPersons.Count() != e.DetectedFaces.Count())
                {
                    this.helpButton.Visibility = Visibility.Visible;
                }
                else
                {
                    this.helpButton.Visibility = Visibility.Collapsed;
                }
                this.lastIdentifiedPersonSample = e.DetectedFaces.Select(f => new Tuple<Face, IdentifiedPerson>(f, e.IdentifiedPersons.FirstOrDefault(p => p.FaceId == f.FaceId)));
            }

            if (!e.SimilarFaceMatches.Any())
            {
                this.lastSimilarPersistedFaceSample = null;
            }
            else
            {
                this.lastSimilarPersistedFaceSample = e.SimilarFaceMatches;
            }

            this.UpdateDemographics(e);

            this.debugText.Text = string.Format("Latency: {0}ms", (int)(DateTime.Now - start).TotalMilliseconds);
            this.cur_latency = (int)(DateTime.Now - start).TotalMilliseconds;
            this.isProcessingPhoto = false;
        }
        
        private async void Speak(string text)
        {
            synthesizer.Voice = (SpeechSynthesizer.AllVoices.First(x => x.Gender == VoiceGender.Female && x.Language.Contains("zh-TW")));
            if (!string.IsNullOrEmpty(text))
            {
                try
                {
                    SpeechSynthesisStream synthesisStream = await synthesizer.SynthesizeTextToStreamAsync(text);
                    media.AutoPlay = true;
                    media.SetSource(synthesisStream, synthesisStream.ContentType);
                    media.Play();
                }
                catch (System.IO.FileNotFoundException)
                {
                    var messageDialog = new Windows.UI.Popups.MessageDialog("Media player components unavailable");
                    await messageDialog.ShowAsync();
                }
                catch (Exception)
                {
                    // If the text is unable to be synthesized, throw an error message to the user.
                    var messageDialog = new Windows.UI.Popups.MessageDialog("Unable to synthesize text");
                    await messageDialog.ShowAsync();
                }
            }
        }

        private string GetGreettingFromFaces(ImageAnalyzer img)
        {
            if (img.IdentifiedPersons.Any())
            {
                string names = img.IdentifiedPersons.Count() > 1 ? string.Join(", ", img.IdentifiedPersons.Select(p => p.Person.Name)) : img.IdentifiedPersons.First().Person.Name;
                this.greetingTextBlock.Foreground = new SolidColorBrush(Windows.UI.Colors.GreenYellow);
                this.weather.Visibility = Visibility.Visible;
                this.weatherTextBlock.Visibility = Visibility.Visible;
                if (img.DetectedFaces.Count() > img.IdentifiedPersons.Count())
                {
                    //Speak(string.Format("歡迎回來, {0}和他的夥伴們!\n您可以使用以下的功能。", names));
                    return string.Format("歡迎回來, {0}和他的夥伴們!\n您可以使用以下的功能。", names);
                }
                else
                {
                    //Speak(string.Format("歡迎回來, {0}! \n您可以使用以下的功能。", names));
                    return string.Format("歡迎回來, {0}! \n您可以使用以下的功能。", names);
                }
            }
            else
            {
                this.greetingTextBlock.Foreground = new SolidColorBrush(Windows.UI.Colors.Yellow);
                this.weather.Visibility = Visibility.Collapsed;
                this.weatherTextBlock.Visibility = Visibility.Collapsed;
                if (img.DetectedFaces.Count() > 1)
                {
                    return "抱歉，無法認出你們任何人的名字...";
                }
                else
                {
                    return "抱歉，無法認出您的名字...";
                }
            }
        }

        private string Greet_action(ImageAnalyzer img)
        {
            if (img.IdentifiedPersons.Any())
            {
                string names = img.IdentifiedPersons.Count() > 1 ? string.Join(", ", img.IdentifiedPersons.Select(p => p.Person.Name)) : img.IdentifiedPersons.First().Person.Name;
                this.greetingTextBlock.Foreground = new SolidColorBrush(Windows.UI.Colors.GreenYellow);
                this.weather.Visibility = Visibility.Visible;
                this.weatherTextBlock.Visibility = Visibility.Visible;
                if (img.DetectedFaces.Count() > img.IdentifiedPersons.Count())
                {
                    //Speak(string.Format("歡迎回來, {0}和他的夥伴們!\n您可以使用以下的功能。", names));
                    return string.Format("歡迎回來, {0}和他的夥伴們!\n您可以使用以下的功能。", names);
                }
                else
                {
                    //Speak(string.Format("歡迎回來, {0}! \n您可以使用以下的功能。", names));
                    return string.Format("歡迎回來, {0}! \n您可以使用以下的功能。", names);
                }
            }
            else
            {
                this.greetingTextBlock.Foreground = new SolidColorBrush(Windows.UI.Colors.Yellow);
                this.weather.Visibility = Visibility.Collapsed;
                this.weatherTextBlock.Visibility = Visibility.Collapsed;
                if (img.DetectedFaces.Count() > 1)
                {
                    return "抱歉，無法認出你們任何人的名字...";
                }
                else
                {
                    return "抱歉，無法認出您的名字...";
                }
            }
        }

        private void UpdateUIForNoFacesDetected()
        {
            this.greetingTextBlock.Text = "站在螢幕前以開始偵測";
            this.greetingTextBlock.Foreground = new SolidColorBrush(Windows.UI.Colors.White);
            this.weather.Visibility = Visibility.Collapsed;
            this.weatherTextBlock.Text = "";
            this.weatherTextBlock.Visibility = Visibility.Collapsed;
        }

        private void ShowTimelineFeedbackForNoFaces()
        {
            this.emotionDataTimelineControl.DrawEmotionData(new EmotionScores { Neutral = 1 });
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            EnterKioskMode();
            dispatcher = CoreWindow.GetForCurrentThread().Dispatcher;
            initMicrophone();
            if (string.IsNullOrEmpty(SettingsHelper.Instance.EmotionApiKey) || string.IsNullOrEmpty(SettingsHelper.Instance.FaceApiKey))
            {
                await new MessageDialog("缺少臉部或情緒分析金鑰。請至設定頁面以完成輸入。", "缺乏金鑰").ShowAsync();
            }
            else
            {
                FaceListManager.FaceListsUserDataFilter = SettingsHelper.Instance.WorkspaceKey + "_RealTime";
                await FaceListManager.Initialize();

                await ResetDemographicsData();
                this.UpdateDemographicsUI();

                await this.saveControl.StartStreamAsync(isForRealTimeProcessing: true);
                this.StartProcessingLoop();
            }

            base.OnNavigatedTo(e);
        }

        private void UpdateDemographics(ImageAnalyzer img)
        {
            if (this.lastSimilarPersistedFaceSample != null)
            {
                bool demographicsChanged = false;
                // Update the Visitor collection (either add new entry or update existing)
                int temp_count = 0;
                List<string> greetname = new List<string>();
                foreach (var item in this.lastSimilarPersistedFaceSample)
                {
                    Visitor visitor;
                    var CurTime = DateTime.Now;
                    if (this.visitors.TryGetValue(item.SimilarPersistedFace.PersistedFaceId, out visitor))
                    {
                        try
                        {
                            visitor.Date = CurTime.Date.ToString("yyyy/MM/dd");
                            visitor.Hour = CurTime.Hour;
                            visitor.Count++;
                            if (this.lastIdentifiedPersonSample != null && this.lastIdentifiedPersonSample.Count() > temp_count && this.lastIdentifiedPersonSample.ElementAt(temp_count)!= null && this.lastIdentifiedPersonSample.ElementAt(temp_count).Item2 != null && this.lastIdentifiedPersonSample.ElementAt(temp_count).Item2.Person != null)
                            {
                                visitor.Name = this.lastIdentifiedPersonSample.ElementAt(temp_count).Item2.Person.Name;
                            }
                            if (visitor.Name != null)
                            {
                                greetname.Add(visitor.Name);
                            }
                            if (this.lastEmotionSample != null && this.lastEmotionSample.Count() > temp_count && this.lastEmotionSample.ElementAt(temp_count)!=null)
                            {
                                Emotion emo = this.lastEmotionSample.ElementAt(temp_count);
                                visitor.Smile = Math.Round(emo.Scores.Happiness, 4);
                            }
                            var messageString = JsonConvert.SerializeObject(visitor);
                            Task.Run(async () => { await AzureIoTHub.SendSQLToCloudMessageAsync(messageString); });
                        }
                        catch(NullReferenceException e)
                        {
                            this.debugText.Text = string.Format("NullRefenrenceException source at 1: {0}", e.Source);
                        }
                    }
                    else
                    {
                        try
                        {
                            demographicsChanged = true;
                            double smile=0;
                            if (this.lastEmotionSample != null && this.lastEmotionSample.Count() > temp_count && this.lastEmotionSample.ElementAt(temp_count)!=null)
                            {
                                Emotion emo = this.lastEmotionSample.ElementAt(temp_count);
                                smile = Math.Round(emo.Scores.Happiness, 4);
                            }
                            else
                            {
                                smile = 0;
                            }
                            int male = 1;
                            double age = item.Face.FaceAttributes.Age;
                            if (string.Compare(item.Face.FaceAttributes.Gender, "male", StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                male = 0;
                            }
                            if (this.lastIdentifiedPersonSample != null && this.lastIdentifiedPersonSample.Count() > temp_count && this.lastIdentifiedPersonSample.ElementAt(temp_count)!=null && this.lastIdentifiedPersonSample.ElementAt(temp_count).Item2!=null && this.lastIdentifiedPersonSample.ElementAt(temp_count).Item2.Person!=null )
                            {
                                string name = this.lastIdentifiedPersonSample.ElementAt(temp_count).Item2.Person.Name;
                                if (name != null)
                                {
                                    visitor = new Visitor { UniqueId = item.SimilarPersistedFace.PersistedFaceId, Count = 1, Gender = male, Age = age, Smile = smile, Date = CurTime.Date.ToString("yyyy/MM/dd"), Hour = CurTime.Hour, Name = name, Device = deviceName };
                                    greetname.Add(name);
                                }
                                else
                                {
                                    visitor = new Visitor { UniqueId = item.SimilarPersistedFace.PersistedFaceId, Count = 1, Gender = male, Age = age, Smile = smile, Date = CurTime.Date.ToString("yyyy/MM/dd"), Hour = CurTime.Hour, Name = name, Device = deviceName };
                                }
                            }
                            else
                            {
                                visitor = new Visitor { UniqueId = item.SimilarPersistedFace.PersistedFaceId, Count = 1, Gender = male, Age = age, Smile = smile, Date = CurTime.Date.ToString("yyyy/MM/dd"), Hour = CurTime.Hour, Name = null, Device = deviceName};
                            }
                            this.visitors.Add(visitor.UniqueId, visitor);
                            this.demographics.Visitors.Add(visitor);

                            // Update the demographics stats. We only do it for new visitors to avoid double counting.
                            AgeDistribution genderBasedAgeDistribution = null;
                            if (string.Compare(item.Face.FaceAttributes.Gender, "male", StringComparison.OrdinalIgnoreCase) == 0)
                            {
                                this.demographics.OverallMaleCount++;
                                genderBasedAgeDistribution = this.demographics.AgeGenderDistribution.MaleDistribution;
                            }
                            else
                            {
                                this.demographics.OverallFemaleCount++;
                                genderBasedAgeDistribution = this.demographics.AgeGenderDistribution.FemaleDistribution;
                            }

                            if (item.Face.FaceAttributes.Age < 16)
                            {
                                genderBasedAgeDistribution.Age0To15++;
                            }
                            else if (item.Face.FaceAttributes.Age < 20)
                            {
                                genderBasedAgeDistribution.Age16To19++;
                            }
                            else if (item.Face.FaceAttributes.Age < 30)
                            {
                                genderBasedAgeDistribution.Age20s++;
                            }
                            else if (item.Face.FaceAttributes.Age < 40)
                            {
                                genderBasedAgeDistribution.Age30s++;
                            }
                            else if (item.Face.FaceAttributes.Age < 50)
                            {
                                genderBasedAgeDistribution.Age40s++;
                            }
                            else
                            {
                                genderBasedAgeDistribution.Age50sAndOlder++;
                            }

                            var messageString = JsonConvert.SerializeObject(visitor);
                            Task.Run(async () => { await AzureIoTHub.SendSQLToCloudMessageAsync(messageString); });
                        }
                        catch (NullReferenceException e)
                        {
                            this.debugText.Text = string.Format("NullRefenrenceException source at 2: {0}", e.Source);
                        }
                    }

                    temp_count++;
                }
                if(greetname.Count > 0)
                {
                    string greetinglist = "";
                    foreach(var name in greetname)
                    {
                        greetinglist += name + ",";
                    }
                    if (GreetVisitor){
                        Speak(string.Format("你好{0}", greetinglist));
                        if (this.lastSimilarPersistedFaceSample.Count() > greetinglist.Count()){
                            otherTextBlock.Text = string.Format("你好, {0}和他(她)的夥伴們!", greetinglist);
                        }
                        else
                        {
                            otherTextBlock.Text = string.Format("你好, {0}!", greetinglist);
                        }
                        GreetVisitor = false;
                    }
                }
                if (demographicsChanged)
                {
                    this.ageGenderDistributionControl.UpdateData(this.demographics);
                }

                this.overallStatsControl.UpdateData(this.demographics);
            }
        }

        private void UpdateDemographicsUI()
        {
            this.ageGenderDistributionControl.UpdateData(this.demographics);
            this.overallStatsControl.UpdateData(this.demographics);
        }

        private async Task ResetDemographicsData()
        {
            this.initializingUI.Visibility = Visibility.Visible;
            this.initializingProgressRing.IsActive = true;

            this.demographics = new DemographicsData
            {
                StartTime = DateTime.Now,
                AgeGenderDistribution = new AgeGenderDistribution { FemaleDistribution = new AgeDistribution(), MaleDistribution = new AgeDistribution() },
                Visitors = new List<Visitor>()
            };

            this.visitors.Clear();
            await FaceListManager.ResetFaceLists();

            this.initializingUI.Visibility = Visibility.Collapsed;
            this.initializingProgressRing.IsActive = false;
        }

        public async Task HandleApplicationShutdownAsync()
        {
            await ResetDemographicsData();
        }

        private void EnterKioskMode()
        {
            ApplicationView view = ApplicationView.GetForCurrentView();
            if (!view.IsFullScreenMode)
            {
                view.TryEnterFullScreenMode();
            }
        }

        protected override async void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            this.isProcessingLoopInProgress = false;
            Window.Current.Activated -= CurrentWindowActivationStateChanged;
            this.saveControl.CameraAspectRatioChanged -= CameraControl_CameraAspectRatioChanged;

            await this.ResetDemographicsData();

            await this.saveControl.StopStreamAsync();
            base.OnNavigatingFrom(e);
        }

        private void OnPageSizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.UpdateCameraHostSize();
        }

        private void UpdateCameraHostSize()
        {
            this.cameraHostGrid.Width = this.cameraHostGrid.ActualHeight * (this.saveControl.CameraAspectRatio != 0 ? this.saveControl.CameraAspectRatio : 1.777777777777);
        }

        public EmotionScores GetLastEmotionForFace(BitmapBounds faceBox)
        {
            if (this.lastEmotionSample == null || !this.lastEmotionSample.Any())
            {
                return null;
            }

            return this.lastEmotionSample.OrderBy(f => Math.Abs(faceBox.X - f.FaceRectangle.Left) + Math.Abs(faceBox.Y - f.FaceRectangle.Top)).First().Scores;
        }

        public Face GetLastFaceAttributesForFace(BitmapBounds faceBox)
        {
            if (this.lastDetectedFaceSample == null || !this.lastDetectedFaceSample.Any())
            {
                return null;
            }

            return Util.FindFaceClosestToRegion(this.lastDetectedFaceSample, faceBox);
        }

        public IdentifiedPerson GetLastIdentifiedPersonForFace(BitmapBounds faceBox)
        {
            if (this.lastIdentifiedPersonSample == null || !this.lastIdentifiedPersonSample.Any())
            {
                return null;
            }

            Tuple<Face, IdentifiedPerson> match =
                this.lastIdentifiedPersonSample.Where(f => Util.AreFacesPotentiallyTheSame(faceBox, f.Item1.FaceRectangle))
                                               .OrderBy(f => Math.Abs(faceBox.X - f.Item1.FaceRectangle.Left) + Math.Abs(faceBox.Y - f.Item1.FaceRectangle.Top)).FirstOrDefault();
            if (match != null)
            {
                return match.Item2;
            }

            return null;
        }

        public SimilarPersistedFace GetLastSimilarPersistedFaceForFace(BitmapBounds faceBox)
        {
            if (this.lastSimilarPersistedFaceSample == null || !this.lastSimilarPersistedFaceSample.Any())
            {
                return null;
            }

            SimilarFaceMatch match =
                this.lastSimilarPersistedFaceSample.Where(f => Util.AreFacesPotentiallyTheSame(faceBox, f.Face.FaceRectangle))
                                               .OrderBy(f => Math.Abs(faceBox.X - f.Face.FaceRectangle.Left) + Math.Abs(faceBox.Y - f.Face.FaceRectangle.Top)).FirstOrDefault();

            return match?.SimilarPersistedFace;
        }

        private void emotionDataTimelineControl_Loaded(object sender, RoutedEventArgs e)
        {

        }

        private async void button_Click(object sender, RoutedEventArgs e)
        {
            await new MessageDialog("若出現Unknown，則代表您可能不存在於人物群組中，或您現有的照片無法讓我們精準的辨認。\n您可以點擊右側的相機圖案去做影像擷取之功能，以利之後臉部辨識之精確性。", "Help(關於Unknown)").ShowAsync();
        }

        private async void weather_Click(object sender, RoutedEventArgs e)
        {
            this.weatherTextBlock.Text = "Hold on ...";
            WeatherDataServiceFactory obj = WeatherDataServiceFactory.Instance;                     //get instance of weather data service factory

            Location location = new Location();                                                     //create location object
            location.city = "Taipei";

            var tmp = await obj.GetWeatherDataService(location);
            tmp.Main.Temp = tmp.Main.Temp - 273.15;
            this.weatherTextBlock.Text = "國家: " + tmp.Sys.Country.ToString() + "\n城市:   "+ tmp.Name.ToString() + "\n氣溫:   " + Math.Round(tmp.Main.Temp,2).ToString() +  "(攝氏)" + "\n濕度:    "  + tmp.Main.Humidity.ToString() + "%";
            string weatherStr = tmp.Name.ToString() + "現在的氣溫是" + Math.Round(tmp.Main.Temp, 2).ToString() + "攝氏度，濕度是" + tmp.Main.Humidity.ToString() + "百分比";
            Speak(weatherStr);
        }

        private async void weather_action()
        {
            this.otherTextBlock.Text = "Hold on ...";
            WeatherDataServiceFactory obj = WeatherDataServiceFactory.Instance;                     //get instance of weather data service factory

            Location location = new Location();                                                     //create location object
            location.city = "Taipei";

            var tmp = await obj.GetWeatherDataService(location);
            tmp.Main.Temp = tmp.Main.Temp - 273.15;
            this.otherTextBlock.Text = "國家: " + tmp.Sys.Country.ToString() + "\n城市:   " + tmp.Name.ToString() + "\n氣溫:   " + Math.Round(tmp.Main.Temp, 2).ToString() + "(攝氏)" + "\n濕度:    " + tmp.Main.Humidity.ToString() + "%";
            string weatherStr = tmp.Name.ToString() + "現在的氣溫是" + Math.Round(tmp.Main.Temp, 2).ToString() + "攝氏度，濕度是" + tmp.Main.Humidity.ToString() + "百分比";
            Speak(weatherStr);
        }

        private async void Microphone_Click(object sender, RoutedEventArgs e)
        {
            // Disable the UI while recognition is occurring, and provide feedback to the user about current state.
            Microphone.IsEnabled = false;
            otherTextBlock.Text = "";
            Debug.WriteLine(" listening for speech...");

            // Start recognition.
            try
            {
                // Save the recognition operation so we can cancel it (as it does not provide a blocking
                // UI, unlike RecognizeWithAsync()
                SpeechRecognitionResult speechRecognitionResult = await speechRecognizer.RecognizeAsync();
                // If successful, display the recognition result. A cancelled task should do nothing.
                if (speechRecognitionResult.Status == SpeechRecognitionResultStatus.Success)
                {
                    Debug.WriteLine(speechRecognitionResult.Text);
                    speechTextBlock.Text = "我聽到你說:" + speechRecognitionResult.Text;
                    //start checking for intents
                    processSpeech(speechRecognitionResult.Text);
                }
                else
                {
                    Debug.WriteLine("Speech Recognition Failed, Status: {0}", speechRecognitionResult.Status.ToString());
                }
            }
            catch (TaskCanceledException exception)
            {
                // TaskCanceledException will be thrown if you exit the scenario while the recognizer is actively
                // processing speech. Since this happens here when we navigate out of the scenario, don't try to 
                // show a message dialog for this exception.
                System.Diagnostics.Debug.WriteLine("TaskCanceledException caught while recognition in progress (can be ignored):");
                System.Diagnostics.Debug.WriteLine(exception.ToString());
            }
            catch (Exception exception)
            {
                // Handle the speech privacy policy error.
                if ((uint)exception.HResult == HResultPrivacyStatementDeclined)
                {
                    Debug.WriteLine("The privacy statement was declined.");
                }
                else
                {
                    var messageDialog = new Windows.UI.Popups.MessageDialog(exception.Message, "Exception");
                    await messageDialog.ShowAsync();
                }
            }

            // Reset UI state.
            Microphone.IsEnabled = true;
        }

        private async void processSpeech(string str)
        {
            using (var webClient = new Windows.Web.Http.HttpClient())
            {
                //string ip_address = GetLocalIp();
                var uri = new Uri("https://westus.api.cognitive.microsoft.com/luis/v2.0/apps/89e36123-823b-413a-8754-c838f378f758?subscription-key=96647e513e3a43abad18c99343e8c124&timezoneOffset=0&verbose=true&q="+str);
                LUISobj result;
                try
                {
                    var json = await webClient.GetStringAsync(uri);
                    // Now parse with JSON.Net
                    result = JsonConvert.DeserializeObject<LUISobj>(json);
                    if(result.topScoringIntent.score > 0.5)
                    {
                        Debug.WriteLine(result.topScoringIntent.intent);
                        switch (result.topScoringIntent.intent)
                        {
                            case "Weather":
                                speechTextBlock.Text = speechTextBlock.Text + "\n\n我會顯示天氣給你看~\n";
                                weather_action();
                                break;
                            case "Greet":
                                speechTextBlock.Text = speechTextBlock.Text + "\n\n我會跟你問好 =) \n";
                                GreetVisitor = true;
                                break;
                            case "Camera":
                                speechTextBlock.Text = speechTextBlock.Text + "\n\n我會幫你拍照，然後存下來! \n";
                                saveControl.SavePhoto();
                                break;
                            case "Emotion":
                                break;
                            case "Translate":
                                speechTextBlock.Text = speechTextBlock.Text + "\n\n我會幫你翻譯! \n";
                                break;
                            default:
                                break;
                        }
                    }
                    else
                    {
                        speechTextBlock.Text = speechTextBlock.Text + "\n\n對不起，我不明白你在說什麼，麻煩你換個方式再說一遍。\n";
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine("Exception {0}.",e.Message);
                    // Details in ex.Message and ex.HResult.       
                }
            }
        }

        private async void window_Click(object sender, RoutedEventArgs e)
        {
            CoreApplicationView newCoreView = CoreApplication.CreateNewView();

            ApplicationView newAppView = null;
            int mainViewId = ApplicationView.GetApplicationViewIdForWindow(
              CoreApplication.MainView.CoreWindow);

            await newCoreView.Dispatcher.RunAsync(
              CoreDispatcherPriority.Normal,
              () =>
              {
                  newAppView = ApplicationView.GetForCurrentView();
                  Window.Current.Content = new MainPage1();
                  Window.Current.Activate();
              });

            await ApplicationViewSwitcher.TryShowAsStandaloneAsync(
              newAppView.Id,
              ViewSizePreference.UseHalf,
              mainViewId,
              ViewSizePreference.UseHalf);
        }
    }

    [XmlType]
    public class Visitor
    {
        [XmlAttribute]
        public Guid UniqueId { get; set; }

        [XmlAttribute]
        public int Count { get; set; }

        [XmlAttribute]
        public int Gender { get; set; }

        [XmlAttribute]
        public double Age { get; set; }

        [XmlAttribute]
        public double Smile { get; set; }

        [XmlAttribute]
        public string Date { get; set; }

        [XmlAttribute]
        public int Hour { get; set; }

        [XmlAttribute]
        public string Name { get; set; }

        [XmlAttribute]
        public string Device { get; set; }
    }

    [XmlType]
    public class AgeDistribution
    {
        public int Age0To15 { get; set; }
        public int Age16To19 { get; set; }
        public int Age20s { get; set; }
        public int Age30s { get; set; }
        public int Age40s { get; set; }
        public int Age50sAndOlder { get; set; }
    }

    [XmlType]
    public class AgeGenderDistribution
    {
        public AgeDistribution MaleDistribution { get; set; }
        public AgeDistribution FemaleDistribution { get; set; }
    }

    [XmlType]
    [XmlRoot]
    public class DemographicsData
    {
        public DateTime StartTime { get; set; }

        public AgeGenderDistribution AgeGenderDistribution { get; set; }

        public int OverallMaleCount { get; set; }

        public int OverallFemaleCount { get; set; }

        [XmlArrayItem]
        public List<Visitor> Visitors { get; set; }
    }


    public class TopScoringIntent
    {
        public string intent { get; set; }
        public double score { get; set; }
    }

    public class Intent
    {
        public string intent { get; set; }
        public double score { get; set; }
    }

    public class LUISobj
    {
        public string query { get; set; }
        public TopScoringIntent topScoringIntent { get; set; }
        public List<Intent> intents { get; set; }
        public List<object> entities { get; set; }
    }
}