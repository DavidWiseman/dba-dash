﻿CREATE PROC dbo.RunningQueriesForJob_Get(
	@InstanceID INT,
	@SnapshotDateFrom DATETIME2(7),
    @SnapshotDateTo DATETIME2(7),
    @JobID UNIQUEIDENTIFIER=NULL
)
AS
DECLARE @AppName NVARCHAR(128)
SET @AppName = 'SQLAgent - TSQL JobStep (Job ' + CONVERT(VARCHAR,CAST(@JobID AS BINARY(16)),1) + '%'

SELECT InstanceID,
       InstanceDisplayName,
       Duration,
       batch_text,
       text,
       query_plan,
       object_id,
       object_name,
       SnapshotDateUTC,
       session_id,
       command,
       status,
       wait_time,
       wait_type,
       TopSessionWaits,
       blocking_session_id,
       BlockingHierarchy,
       BlockCountRecursive, 
	   BlockWaitTimeRecursiveMs,
       BlockWaitTimeRecursive,
	   BlockCount,
       IsRootBlocker,
	   BlockWaitTimeMs,
       BlockWaitTime,
       cpu_time,
       logical_reads,
       reads,
       writes,
       granted_query_memory_kb,
       percent_complete,
       open_transaction_count,
       transaction_isolation_level,
       login_name,
       host_name,
       database_id,
       database_name,
       database_names,
       program_name,
       job_id,
       job_name,
       client_interface_name,
       start_time_utc,
       last_request_start_time_utc,
       last_request_end_time_utc,
       last_request_duration,
       sleeping_session_idle_time_sec,
       sleeping_session_idle_time,
       sql_handle,
       plan_handle,
       query_hash,
       query_plan_hash,
       [Duration (ms)],
       wait_resource,
       wait_resource_type,
       wait_database_id,
       wait_file_id,
       wait_page_id,
       wait_object_id,
       wait_index_id,
       wait_hobt,
       wait_hash,
       wait_slot,
       wait_is_compile,
       page_type,
       wait_db,
       wait_object,
       wait_file,
       login_time_utc,
       has_plan,
       statement_start_offset,
       statement_end_offset
FROM dbo.RunningQueriesInfo Q
WHERE Q.SnapshotDateUTC >= @SnapshotDateFrom 
AND Q.SnapshotDateUTC < @SnapshotDateTo
AND Q.InstanceID = @InstanceID
AND Q.program_name LIKE @AppName
ORDER BY Q.SnapshotDateUTC