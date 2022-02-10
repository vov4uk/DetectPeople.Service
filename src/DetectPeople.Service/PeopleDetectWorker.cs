using DetectPeople.Contracts.Message;
using DetectPeople.YOLOv5Net;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using NLog;
using RabbitMQ.Client.Events;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DetectPeople.Service
{
    public class PeopleDetectWorker : BackgroundService
    {
        protected readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private readonly ObjectsDetecor objectsDetector;
        private readonly Stopwatch timer = new();
        private PeopleDetectConfig config;
        private HashSet<int> forbiddenObjects;

        public PeopleDetectWorker()
        {
            try
            {
                this.objectsDetector = new ObjectsDetecor();
            }
            catch (Exception e)
            {
                logger.Error(e);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.Info("PeopleDetectWorker started");
            config = GetConfig();

            logger.Info(JsonConvert.SerializeObject(config, Formatting.Indented));

            var objectIds = Objects.GetIds(config.ForbiddenObjects);
            forbiddenObjects = new HashSet<int>(objectIds);

            RabbitMQHelper hikReceiver = new(config.RabbitMQ.HostName, config.RabbitMQ.QueueName, config.RabbitMQ.RoutingKey);
            hikReceiver.Received += Rabbit_Received;
            hikReceiver.Consume();

            var tcs = new TaskCompletionSource<bool>();
            stoppingToken.Register(s =>
            {
                hikReceiver.Close();
                ((TaskCompletionSource<bool>)s).SetResult(true);
            }, tcs);
            await tcs.Task;
            hikReceiver.Received -= Rabbit_Received;
            hikReceiver.Dispose();
            logger.Info("PeopleDetectWorker stopped");
        }

        private PeopleDetectConfig GetConfig()
        {
            var rootDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var configPath = Path.Combine(rootDir, "config.json");
            if (!File.Exists(configPath))
            {
                logger.Error($"\"{configPath}\" does not exist.");
                logger.Info("Use default config");
                return new PeopleDetectConfig { RabbitMQ = new RabbitMQConfig { HostName = "localhost", QueueName = "hik", RoutingKey = "hik" }, ForbiddenObjects = new[] { "car", "train", "bird" }, DrawJunkObjects = true };
            }
            else
            {
                logger.Info(configPath);
                return JsonConvert.DeserializeObject<PeopleDetectConfig>(File.ReadAllText(configPath));
            }
        }

        private bool IsPerson(ObjectDetectResult res, int minHeight, int minWidht)
        {
            if (!forbiddenObjects.Contains(res.Id))
            {
                var rect = res.GetRectangle();
                return rect.Height >= minHeight && rect.Width >= minWidht;
            }
            return false;
        }

        private async void Rabbit_Received(object sender, BasicDeliverEventArgs ea)
        {
            try
            {

                var body = Encoding.UTF8.GetString(ea.Body.ToArray());
                DetectPeopleMessage msg = JsonConvert.DeserializeObject<DetectPeopleMessage>(body);
                logger.Debug("[x] Received {0}", body);

                if (!File.Exists(msg.OldFilePath))
                {
                    logger.Warn($"{msg.OldFilePath} not exist");
                    return;
                }

                timer.Restart();
                IReadOnlyList<ObjectDetectResult> objects = await objectsDetector.DetectObjectsAsync(msg.OldFilePath);
                timer.Stop();
                logger.Debug(msg.OldFilePath);
                logger.Debug($"{timer.ElapsedMilliseconds}ms. {objects.Count} objects detected. {string.Join(", ", objects.Select(x => x.Label))}");

                //DrawObjects(msg.OldFilePath, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Test", Path.GetFileName(msg.OldFilePath)), objects);

                if (objects.Any())
                {
                    int minHeight = config.MinPersonHeightPixel;
                    int minWidth = config.MinPersonWidthPixel;
                    using (Image img = Image.FromFile(msg.OldFilePath))
                    {
                        minHeight = Convert.ToInt32(img.Height * config.MinPersonHeightPersentage / 100.0);
                        minWidth = Convert.ToInt32(img.Width * config.MinPersonWidthPersentage / 100.0);
                    }

                    var peoples = objects.Where(x => IsPerson(x, minHeight, minWidth));
                    if (peoples.Any())
                    {
                        if (config.DrawObjects)
                        {
                            DrawObjects(msg.OldFilePath, msg.NewFilePath, objects, config.FillObjectsRectangle);
                        }
                        else
                        {
                            SaveJpg(msg.OldFilePath, msg.NewFilePath);
                        }
                    }
                    else
                    {
                        ProcessJunk(msg, objects);
                    }
                }
                else
                {
                    ProcessJunk(msg, objects);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex);
            }
        }

        private void ProcessJunk(DetectPeopleMessage msg, IReadOnlyList<ObjectDetectResult> objects)
        {
            if (!msg.DeleteJunk)
            {
                if (config.DrawJunkObjects)
                {
                    DrawObjects(msg.OldFilePath, msg.JunkFilePath, objects);
                }
                else
                {
                    SaveJpg(msg.OldFilePath, msg.JunkFilePath);
                }
            }
            File.Delete(msg.OldFilePath);
        }

        private void DrawObjects(string originalPath, string destination, IReadOnlyList<ObjectDetectResult> results, bool fillRectangle = true)
        {
            using (Bitmap bitmap = new Bitmap(originalPath))
            {
                if (results.Any())
                {
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        foreach (var res in results)
                        {
                            // draw predictions
                            var x1 = res.BBox[0];
                            var y1 = res.BBox[1];
                            var x2 = res.BBox[2];
                            var y2 = res.BBox[3];
                            g.DrawRectangle(Pens.Red, x1, y1, x2 - x1, y2 - y1);
                            if (fillRectangle)
                            {
                                using (var brushes = new SolidBrush(Color.FromArgb(50, Color.Red)))
                                {
                                    g.FillRectangle(brushes, x1, y1, x2 - x1, y2 - y1);
                                }
                            }


                            g.DrawString(res.Label + " " + res.Confidence.ToString("0.00"), new Font("Arial", 12), Brushes.Yellow, new PointF(x1, y1));
                        }
                    }
                }

                var parameters = GetCompressParameters();

                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                bitmap.Save(destination, parameters.jpgEncoder, parameters.myEncoderParameters);
            }
        }

        private (ImageCodecInfo jpgEncoder, EncoderParameters myEncoderParameters) GetCompressParameters()
        {
            var jpgEncoder = GetEncoder(ImageFormat.Jpeg);
            System.Drawing.Imaging.Encoder myEncoder = System.Drawing.Imaging.Encoder.Quality;
            var myEncoderParameters = new EncoderParameters(1);
            myEncoderParameters.Param[0] = new EncoderParameter(myEncoder, 25L);

            return (jpgEncoder, myEncoderParameters);
        }

        private void SaveJpg(string source, string destination)
        {
            try
            {
                CompressImage(source, destination);
                File.Delete(source);
            }
            catch (Exception ex)
            {
                logger.Error("Error saving file '" + destination + ex.ToString());
            }
        }

        private void CompressImage(string source, string destination)
        {
            using (Bitmap bitmap = new Bitmap(source))
            {
                var parameters = GetCompressParameters();

                if (File.Exists(destination))
                {
                    File.Delete(destination);
                }

                bitmap.Save(destination, parameters.jpgEncoder, parameters.myEncoderParameters);
            }
        }

        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }
    }
}