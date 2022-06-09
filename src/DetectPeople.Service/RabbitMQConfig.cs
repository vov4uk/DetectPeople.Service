namespace DetectPeople.Service
{
    public class RabbitMQConfig
    {
        public string HostName { get; set; }

        public string QueueName { get; set; }

        public string RoutingKey { get; set; }
    }
}
