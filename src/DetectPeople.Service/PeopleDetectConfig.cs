namespace DetectPeople.Service
{
    public class PeopleDetectConfig
    {
        public RabbitMQConfig RabbitMQ { get; set; }

        public bool DrawJunkObjects { get; set; } = true;
        public bool DrawObjects { get; set; }
        public bool FillObjectsRectangle { get; set; } = false;

        public string[] ForbiddenObjects { get; set; }

        public double MinPersonHeightPersentage { get; set; } = 13.1;
        public double MinPersonWidthPersentage { get; set; } = 3.7;
        public int MinPersonHeightPixel { get; set; } = 200;
        public int MinPersonWidthPixel { get; set; } = 100;
    }

    public class RabbitMQConfig
    {
        public string HostName { get; set; }
        public string QueueName { get; set; }
        public string RoutingKey { get; set; }
    }
}
