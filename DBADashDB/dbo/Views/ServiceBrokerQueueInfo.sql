CREATE VIEW dbo.ServiceBrokerQueueInfo
AS
/*
	Service Broker queues with their status.
	QueueStatus: 1=Critical, 2=Warning, 4=OK.  Issues describes what caused a warning or critical status.
	Critical:
		- Receive disabled.  Usually a poison message - 5 consecutive rollbacks of a RECEIVE disables the queue.
		- Enqueue disabled (queue STATUS = OFF).  Messages can't be added to the queue.
		- Activation procedure doesn't exist and there are messages waiting.
		- Queue depth or the age of the oldest message waiting at or over the critical threshold.
	Warning:
		- Activation is disabled with messages waiting.
		- Activation procedure doesn't exist (no messages waiting).
		- Activation has been notified for 10 mins or more with messages waiting, but nothing has received from the queue.  The activation procedure
		  probably isn't issuing a RECEIVE on this queue (or an external activator isn't running).
		- Queue depth or the age of the oldest message waiting at or over the warning threshold.
	ReadersStatus is a warning where every activated reader is busy with messages still waiting.  This isn't included in QueueStatus as it can be
	normal for a busy queue, but it's worth knowing if the queue depth is also growing.

	The age of the oldest message is what shows a queue isn't being processed - a queue can be deep but draining, or shallow and stuck.
	It's not collected for queues over 100,000 messages (or that the collection can't read), where the depth thresholds still apply.
	message_count is NULL for a queue with RETENTION = ON that couldn't be read as retained messages make the depth meaningless.

	Instances where the collection has been disabled are excluded as the data is no longer current.
*/
SELECT Q.QueueID,
	Q.InstanceID,
	Q.DatabaseID,
	I.InstanceDisplayName,
	D.name AS DB,
	Q.schema_name,
	Q.queue_name,
	D.is_broker_enabled,
	Q.is_receive_enabled,
	Q.is_enqueue_enabled,
	Q.is_retention_enabled,
	Q.is_activation_enabled,
	Q.is_poison_message_handling_enabled,
	Q.activation_procedure,
	Q.activation_procedure_exists,
	Q.max_readers,
	Q.activated_tasks,
	Q.message_count,
	Q.oldest_message_time,
	Calc.OldestMessageAgeMins,
	Q.monitor_state,
	Q.monitor_tasks_waiting,
	Q.monitor_last_activated_time,
	Q.monitor_last_empty_rowset_time,
	Calc.NotifiedMins,
	Q.SnapshotDate,
	THRES.QueueDepthWarningThreshold,
	THRES.QueueDepthCriticalThreshold,
	THRES.QueueAgeWarningThreshold,
	THRES.QueueAgeCriticalThreshold,
	THRES.ThresholdConfigurationLevel,
	Chk.QueueDepthStatus,
	Chk.QueueAgeStatus,
	CASE WHEN Q.is_activation_enabled = 1 AND Q.max_readers > 0 AND Q.activated_tasks >= Q.max_readers AND Q.message_count > 0 THEN 2 ELSE 4 END AS ReadersStatus,
	CASE WHEN Chk.ReceiveDisabled = 1 OR Chk.EnqueueDisabled = 1 OR (Chk.ActivationProcMissing = 1 AND Q.message_count > 0) OR Chk.QueueDepthStatus = 1 OR Chk.QueueAgeStatus = 1 THEN 1
		WHEN Chk.ActivationDisabledWithMessages = 1 OR Chk.ActivationProcMissing = 1 OR Chk.ActivationNotReceiving = 1 OR Chk.QueueDepthStatus = 2 OR Chk.QueueAgeStatus = 2 THEN 2
		ELSE 4 END AS QueueStatus,
	STUFF(CONCAT(
		CASE WHEN Chk.ReceiveDisabled = 1 THEN '; Queue is disabled for receive.  Usually caused by a poison message (5 consecutive rollbacks).' END,
		CASE WHEN Chk.EnqueueDisabled = 1 THEN '; Queue status is OFF.  Messages can''t be added to the queue.' END,
		CASE WHEN Chk.ActivationProcMissing = 1 THEN '; Activation procedure doesn''t exist.' END,
		CASE WHEN Chk.ActivationDisabledWithMessages = 1 THEN '; Activation is disabled with messages waiting.' END,
		CASE WHEN Chk.ActivationNotReceiving = 1 THEN CONCAT('; Activation was notified ', Calc.NotifiedMins, ' mins ago but nothing has received from the queue.  Check the activation procedure RECEIVEs from this queue.') END,
		CASE WHEN Chk.QueueAgeStatus IN(1,2) THEN CONCAT('; Oldest message has been waiting ', Calc.OldestMessageAgeMins, ' mins.') END,
		CASE WHEN Chk.QueueDepthStatus IN(1,2) THEN '; Queue depth over threshold.' END
		),1,2,'') AS Issues
FROM dbo.ServiceBrokerQueues Q
JOIN dbo.Instances I ON I.InstanceID = Q.InstanceID
JOIN dbo.Databases D ON D.DatabaseID = Q.DatabaseID
OUTER APPLY(SELECT TOP(1) T.QueueDepthWarningThreshold,
						T.QueueDepthCriticalThreshold,
						T.QueueAgeWarningThreshold,
						T.QueueAgeCriticalThreshold,
						CASE WHEN T.queue_name <> '' THEN 'Queue' WHEN T.DatabaseID <> -1 THEN 'Database' WHEN T.InstanceID <> -1 THEN 'Instance' ELSE 'Root' END AS ThresholdConfigurationLevel
			FROM dbo.ServiceBrokerThresholds T
			WHERE (T.InstanceID = Q.InstanceID OR T.InstanceID = -1)
			AND (T.DatabaseID = Q.DatabaseID OR T.DatabaseID = -1)
			AND (T.queue_name = Q.queue_name OR T.queue_name = '')
			ORDER BY T.InstanceID DESC, T.DatabaseID DESC, T.queue_name DESC
			) THRES
CROSS APPLY(SELECT CASE WHEN Q.message_count > 0 THEN DATEDIFF(mi,Q.oldest_message_time,Q.SnapshotDate) END AS OldestMessageAgeMins,
				CASE WHEN Q.monitor_state = 'NOTIFIED' THEN DATEDIFF(mi,Q.monitor_last_activated_time,Q.SnapshotDate) END AS NotifiedMins
			) Calc
CROSS APPLY(SELECT CASE WHEN Q.is_receive_enabled = 0 THEN 1 ELSE 0 END AS ReceiveDisabled,
				CASE WHEN Q.is_enqueue_enabled = 0 THEN 1 ELSE 0 END AS EnqueueDisabled,
				CASE WHEN Q.is_activation_enabled = 1 AND Q.activation_procedure_exists = 0 THEN 1 ELSE 0 END AS ActivationProcMissing,
				CASE WHEN Q.is_activation_enabled = 0 AND Q.activation_procedure IS NOT NULL AND Q.message_count > 0 THEN 1 ELSE 0 END AS ActivationDisabledWithMessages,
				CASE WHEN Calc.NotifiedMins >= 10 AND Q.message_count > 0 AND Q.is_receive_enabled = 1 THEN 1 ELSE 0 END AS ActivationNotReceiving,
				CASE WHEN Q.message_count IS NULL THEN 3
					WHEN Q.message_count >= THRES.QueueDepthCriticalThreshold THEN 1
					WHEN Q.message_count >= THRES.QueueDepthWarningThreshold THEN 2
					WHEN THRES.QueueDepthWarningThreshold IS NULL AND THRES.QueueDepthCriticalThreshold IS NULL THEN 3
					ELSE 4 END AS QueueDepthStatus,
				CASE WHEN Calc.OldestMessageAgeMins >= THRES.QueueAgeCriticalThreshold THEN 1
					WHEN Calc.OldestMessageAgeMins >= THRES.QueueAgeWarningThreshold THEN 2
					WHEN THRES.QueueAgeWarningThreshold IS NULL AND THRES.QueueAgeCriticalThreshold IS NULL THEN 3
					WHEN Calc.OldestMessageAgeMins IS NULL AND Q.message_count > 0 THEN 3 /* Not collected */
					ELSE 4 END AS QueueAgeStatus
			) Chk
WHERE Q.IsActive = 1
AND I.IsActive = 1
AND D.IsActive = 1
AND NOT EXISTS(	SELECT 1
				FROM dbo.ScheduleInfo SI
				WHERE SI.InstanceID = Q.InstanceID
				AND SI.Reference = 'ServiceBroker'
				AND SI.IsEnabled = 0
				)
