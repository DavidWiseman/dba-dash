CREATE PROC dbo.ServiceBrokerThresholds_Upd(
	@InstanceID INT,
	@DatabaseID INT,
	@queue_name NVARCHAR(128),
	@QueueDepthWarningThreshold BIGINT,
	@QueueDepthCriticalThreshold BIGINT,
	@QueueAgeWarningThreshold INT,
	@QueueAgeCriticalThreshold INT,
	@TransmissionQueueAgeWarningThreshold INT,
	@TransmissionQueueAgeCriticalThreshold INT,
	@ConversationCountWarningThreshold BIGINT,
	@ConversationCountCriticalThreshold BIGINT,
	@Inherit BIT = 0
)
AS
SET XACT_ABORT ON
IF @Inherit = 1 AND @InstanceID = -1 AND @DatabaseID = -1 AND @queue_name = ''
BEGIN;
	THROW 50000,'Root thresholds can''t be inherited',1;
END

BEGIN TRAN

DELETE T
FROM dbo.ServiceBrokerThresholds T
WHERE T.InstanceID = @InstanceID
AND T.DatabaseID = @DatabaseID
AND T.queue_name = @queue_name

IF @Inherit = 0
BEGIN
	/* Transmission queue and conversations are per database so they don't have queue level thresholds */
	INSERT INTO dbo.ServiceBrokerThresholds(
		InstanceID,
		DatabaseID,
		queue_name,
		QueueDepthWarningThreshold,
		QueueDepthCriticalThreshold,
		QueueAgeWarningThreshold,
		QueueAgeCriticalThreshold,
		TransmissionQueueAgeWarningThreshold,
		TransmissionQueueAgeCriticalThreshold,
		ConversationCountWarningThreshold,
		ConversationCountCriticalThreshold
	)
	VALUES(
		@InstanceID,
		@DatabaseID,
		@queue_name,
		@QueueDepthWarningThreshold,
		@QueueDepthCriticalThreshold,
		@QueueAgeWarningThreshold,
		@QueueAgeCriticalThreshold,
		CASE WHEN @queue_name = '' THEN @TransmissionQueueAgeWarningThreshold END,
		CASE WHEN @queue_name = '' THEN @TransmissionQueueAgeCriticalThreshold END,
		CASE WHEN @queue_name = '' THEN @ConversationCountWarningThreshold END,
		CASE WHEN @queue_name = '' THEN @ConversationCountCriticalThreshold END
	)
END

COMMIT
