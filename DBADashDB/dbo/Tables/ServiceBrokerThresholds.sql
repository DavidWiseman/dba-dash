/*
	Service Broker thresholds.  Configured at root (InstanceID=-1, DatabaseID=-1, queue_name=''), instance, database or queue level.  The most specific row applies.
	Queue depth and age thresholds apply at any level.  Transmission queue and conversation thresholds are for a database so queue level rows don't set them.
*/
CREATE TABLE dbo.ServiceBrokerThresholds(
    InstanceID INT NOT NULL,
    DatabaseID INT NOT NULL,
    queue_name NVARCHAR(128) NOT NULL,
    QueueDepthWarningThreshold BIGINT NULL,
    QueueDepthCriticalThreshold BIGINT NULL,
    QueueAgeWarningThreshold INT NULL, /* Mins - age of the oldest message waiting in the queue */
    QueueAgeCriticalThreshold INT NULL, /* Mins */
    TransmissionQueueAgeWarningThreshold INT NULL, /* Mins */
    TransmissionQueueAgeCriticalThreshold INT NULL, /* Mins */
    ConversationCountWarningThreshold BIGINT NULL,
    ConversationCountCriticalThreshold BIGINT NULL,
    CONSTRAINT PK_ServiceBrokerThresholds PRIMARY KEY(InstanceID,DatabaseID,queue_name)
);
