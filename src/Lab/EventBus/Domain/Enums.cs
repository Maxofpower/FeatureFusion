using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EventBusRabbitMQ.Domain
{
	public enum MessageStoreResult
	{
		Success,
		Duplicate,
		StorageFailed,
		NoSubscribers,
	}
	public enum ProcessingResult
	{
		Success,
		RetryLater,
		PermanentFailure,
		Duplicate
	}
}
