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
using DetectPeople.Contracts.Message;
using DetectPeople.YOLOv5Net;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using NLog;
using RabbitMQ.Client.Events;

namespace DetectPeople.Service
{
    public class PeopleDetectWorker : BackgroundService
    {
        protected readonly ILogger logger = LogManager.GetCurrentClassLogger();
        private readonly ObjectsDetecor objectsDetector;
        private readonly Stopwatch timer = new ();
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
            var version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            logger.Info($"PeopleDetectWorker started v.{version}");
            config = GetConfig();

            logger.Info(JsonConvert.SerializeObject(config, Formatting.Indented));

            int[] objectIds = Objects.GetIds(config.ForbiddenObjects);
            forbiddenObjects = new HashSet<int>(objectIds);

            RabbitMQHelper hikReceiver = new (config.RabbitMQ.HostName, config.RabbitMQ.QueueName, config.RabbitMQ.RoutingKey);
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
                return new PeopleDetectConfig
                {
                    RabbitMQ = new RabbitMQConfig
                    {
                        HostName = "localhost",
                        QueueName = "hik",
                        RoutingKey = "hik"
                    }, ForbiddenObjects = new[] { "car", "train", "bird" },
                    DrawJunkObjects = true
                };
            }
            else
            {
                logger.Info(configPath);
                return JsonConvert.DeserializeObject<PeopleDetectConfig>(File.ReadAllText(configPath));
            }
        }

        private bool IsPerson(ObjectDetectResult detected, int minHeight, int minWidht)
        {
            if (!forbiddenObjects.Contains(detected.Id))
            {
                var rect = detected.GetRectangle();
                return rect.Height >= minHeight && rect.Width >= minWidht;
            }

            return false;
        }

        private async void Rabbit_Received(object sender, BasicDeliverEventArgs ea)
        {
            string sourceFile = string.Empty;
            try
            {
                DetectPeopleMessage msg = JsonConvert.DeserializeObject<DetectPeopleMessage>(Encoding.UTF8.GetString(ea.Body.ToArray()));

                sourceFile = msg.OldFilePath;
                if (!File.Exists(sourceFile))
                {
                    logger.Warn($"{sourceFile} not exist");
                    return;
                }

                timer.Restart();
                IReadOnlyList<ObjectDetectResult> objects = await objectsDetector.DetectObjectsAsync(sourceFile);
                timer.Stop();
                logger.Debug($"{sourceFile} {timer.ElapsedMilliseconds}ms. {objects.Count} objects detected. {string.Join(", ", objects.Select(x => x.Label))}");

                // DrawObjects(msg.OldFilePath, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Test", Path.GetFileName(msg.OldFilePath)), objects);
                if (objects.Any())
                {
                    int minHeight = config.MinPersonHeightPixel;
                    int minWidth = config.MinPersonWidthPixel;
                    using (Image img = Image.FromFile(sourceFile))
                    {
                        minHeight = Convert.ToInt32(img.Height * config.MinPersonHeightPersentage / 100.0);
                        minWidth = Convert.ToInt32(img.Width * config.MinPersonWidthPersentage / 100.0);
                    }

                    bool hasPeoples = objects.Any(x => IsPerson(x, minHeight, minWidth));
                    if (hasPeoples)
                    {
                        if (config.DrawObjects)
                        {
                            var tmp = Path.GetTempFileName();
                            DrawObjects(sourceFile, tmp, objects, config.FillObjectsRectangle);
                            SaveJpg(tmp, msg.NewFilePath);
                            DeleteFile(sourceFile);
                        }
                        else
                        {
                            SaveJpg(sourceFile, msg.NewFilePath);
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
                DeleteFile(sourceFile);
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

            DeleteFile(msg.OldFilePath);
        }

        private void DrawObjects(string originalPath, string destination, IReadOnlyList<ObjectDetectResult> results, bool fillRectangle = true)
        {
            using (Bitmap bitmap = new Bitmap(originalPath))
            {
                if (results.Any())
                {
                    using (var img = Graphics.FromImage(bitmap))
                    {
                        foreach (var result in results)
                        {
                            var rectangle = result.GetRectangle();
                            img.DrawRectangle(Pens.Red, rectangle);
                            if (fillRectangle)
                            {
                                using (var brushes = new SolidBrush(Color.FromArgb(50, Color.Red)))
                                {
                                    img.FillRectangle(brushes, rectangle);
                                }
                            }

                            img.DrawString(
                                $"{result.Label} {result.Confidence:0.00}",
                                new Font("Arial", 12),
                                Brushes.Yellow,
                                new PointF(rectangle.X, rectangle.Y));
                        }
                    }
                }

                var parameters = GetCompressParameters();
                DeleteFile(destination);

                bitmap.Save(destination, parameters.jpgEncoder, parameters.encoderParameters);
            }
        }

        private (ImageCodecInfo jpgEncoder, EncoderParameters encoderParameters) GetCompressParameters()
        {
            var jpgEncoder = GetEncoder(ImageFormat.Jpeg);
            var encoderParameters = new EncoderParameters(1);
            encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 25L);

            return (jpgEncoder, encoderParameters);
        }

        private void SaveJpg(string source, string destination)
        {
            try
            {
                CompressImage(source, destination);
                DeleteFile(source);
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
                DeleteFile(destination);
                bitmap.Save(destination, parameters.jpgEncoder, parameters.encoderParameters);
            }
        }

        private ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            return codecs.FirstOrDefault(c => c.FormatID == format.Guid);
        }

        private void DeleteFile(string filepath)
        {
            try
            {
                File.Delete(filepath);
            }
            catch (Exception)
            {
            }
        }
    }
}