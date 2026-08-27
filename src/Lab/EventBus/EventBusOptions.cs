namespace EventBusRabbitMQ
{

    public class EventBusOptions
    {
        public bool EnableDeduplication { get; set; } = false;
        public required string SubscriptionClientName { get; set; }
        public int RetryCount { get; set; } = 10;
		public int MessageTTL { get; set; } = 86400000; // 24 hours in ms
        public int MaxRetryCount { get; set; } = 3;

	}
}