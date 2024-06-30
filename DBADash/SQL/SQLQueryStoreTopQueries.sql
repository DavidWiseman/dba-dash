/*
DECLARE @Database SYSNAME='DBADashDB'
DECLARE @SortCol NVARCHAR(128) = 'total_cpu_time_ms';
DECLARE @Top INT=25
DECLARE @FromDate DATETIMEOFFSET(7)=DATEADD(mi,-60,SYSUTCDATETIME())
DECLARE @ToDate DATETIMEOFFSET(7)=SYSUTCDATETIME()
DECLARE @ObjectName NVARCHAR(128) = NULL
DECLARE @ObjectID INT = NULL
DECLARE @NearestInterval BIT
*/
IF NOT EXISTS(
	SELECT 1 
	FROM sys.databases 
	WHERE name = @Database
)
BEGIN
	RAISERROR('Invalid database',11,1)
	RETURN
END
DECLARE @SortSQL NVARCHAR(MAX)
SELECT @SortSQL = CASE WHEN @SortCol IN('total_cpu_time_ms',
											'avg_cpu_time_ms',
											'total_duration_ms',
											'avg_duration_ms',
											'count_executions',
											'max_memory_grant_kb',
											'total_physical_io_reads_kb',
											'avg_physical_io_reads_kb') THEN QUOTENAME(@SortCol) ELSE NULL END
IF @SortSQL IS NULL
BEGIN
	RAISERROR('Invalid sort',11,1)
	RETURN
END
DECLARE @SQL NVARCHAR(MAX)
SET @SQL = CONCAT(N'
USE ', QUOTENAME(@Database),'
DECLARE @interval_from BIGINT
DECLARE @interval_to BIGINT

SELECT  TOP(1) @interval_from= runtime_stats_interval_id
FROM sys.query_store_runtime_stats_interval
WHERE start_time<=@FromDate
ORDER BY start_time DESC

SELECT  TOP(1) @interval_to= runtime_stats_interval_id
FROM sys.query_store_runtime_stats_interval
WHERE end_time>=@ToDate
ORDER BY end_time ASC

SELECT TOP (@Top)
		DB_NAME() AS DB,
		p.query_id query_id,
		q.object_id object_id,
		ISNULL(OBJECT_NAME(q.object_id),'''') object_name,
		qt.query_sql_text query_sql_text,
		ROUND(CONVERT(float, SUM(rs.avg_cpu_time*rs.count_executions))*0.001,2) total_cpu_time_ms,
		ROUND(CONVERT(float, SUM(rs.avg_cpu_time*rs.count_executions))/NULLIF(SUM(rs.count_executions), 0)*0.001,2) avg_cpu_time_ms,
		ROUND(CONVERT(float, SUM(rs.avg_duration*rs.count_executions))*0.001,2) total_duration_ms,
		ROUND(CONVERT(float, SUM(rs.avg_duration*rs.count_executions))/NULLIF(SUM(rs.count_executions), 0)*0.001,2) avg_duration_ms,
		SUM(rs.count_executions) count_executions,
		SUM(rs.count_executions)*60.0/DATEDIFF(s, MIN(MIN(rs.first_execution_time)) OVER(),MAX(MAX(rs.last_execution_time)) OVER()) executions_per_min,
		ROUND(CONVERT(float, MAX(rs.max_query_max_used_memory))*8,2) max_memory_grant_kb,
		ROUND(CONVERT(float, SUM(rs.avg_physical_io_reads*rs.count_executions))*8,2) total_physical_io_reads_kb,
		ROUND(CONVERT(float, SUM(rs.avg_physical_io_reads*rs.count_executions))/NULLIF(SUM(rs.count_executions), 0)*8,2) avg_physical_io_reads_kb,
		COUNT(distinct p.plan_id) num_plans
FROM sys.query_store_runtime_stats rs
JOIN sys.query_store_plan p ON p.plan_id = rs.plan_id
JOIN sys.query_store_query q ON q.query_id = p.query_id
JOIN sys.query_store_query_text qt ON q.query_text_id = qt.query_text_id
WHERE rs.runtime_stats_interval_id >= @interval_from
AND rs.runtime_stats_interval_id <= @interval_to
', CASE WHEN @NearestInterval = 1 THEN '' ELSE 'AND NOT (rs.first_execution_time > @ToDate OR rs.last_execution_time < @FromDate)' END,'
', CASE WHEN @ObjectName IS NOT NULL THEN 'AND OBJECT_NAME(q.object_id) = @ObjectName COLLATE DATABASE_DEFAULT' ELSE '' END,'
', CASE WHEN @ObjectID IS NOT NULL THEN 'AND q.object_id = @ObjectID' ELSE '' END,'
GROUP BY p.query_id, 
		qt.query_sql_text, 
		q.object_id
ORDER BY ',@SortSQL,' DESC')

EXEC sp_executesql @SQL,N'@Top INT,@FromDate DATETIMEOFFSET(7),@ToDate DATETIMEOFFSET(7), @ObjectName NVARCHAR(128),@ObjectID INT',@Top,@FromDate,@ToDate,@ObjectName,@ObjectID