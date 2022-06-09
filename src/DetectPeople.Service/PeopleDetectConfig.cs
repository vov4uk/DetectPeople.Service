namespace DetectPeople.Service
{
    public class PeopleDetectConfig
    {
        public bool DrawJunkObjects { get; set; } = true;

        public bool DrawObjects { get; set; }

        public bool FillObjectsRectangle { get; set; } = false;

        public string[] ForbiddenObjects { get; set; }

        public double MinPersonHeightPersentage { get; set; } = 13.1;

        public int MinPersonHeightPixel { get; set; } = 200;

        public double MinPersonWidthPersentage { get; set; } = 3.7;

        public int MinPersonWidthPixel { get; set; } = 100;

        public RabbitMQConfig RabbitMQ { get; set; }
    }
}
