/*
DECLARE @Database SYSNAME='DBADashDB'
DECLARE @sort_column NVARCHAR(128) = 'total_cpu_time_ms';
DECLARE @Top INT
DECLARE @interval_start_time datetimeoffset(7)
DECLARE @interval_end_time datetimeoffset(7)
SELECT @Top=25,@interval_start_time=DATEADD(mi,-60,SYSUTCDATETIME()),@interval_end_time=SYSUTCDATETIME()
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
SELECT @SortSQL = CASE WHEN @sort_column IN('total_cpu_time_ms',
											'avg_cpu_time_ms',
											'total_duration_ms',
											'avg_duration_ms',
											'count_executions',
											'max_memory_grant_kb',
											'total_physical_io_reads_kb',
											'avg_physical_io_reads_kb') THEN QUOTENAME(@sort_column) ELSE NULL END
IF @SortSQL IS NULL
BEGIN
	RAISERROR('Invalid sort',11,1)
	RETURN
END
DECLARE @SQL NVARCHAR(MAX)
SET @SQL = CONCAT(N'
USE ', QUOTENAME(@Database),'
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
		SUM(rs.count_executions)*60.0/DATEDIFF(s,@interval_start_time,@interval_end_time) executions_per_min,
		ROUND(CONVERT(float, MAX(rs.max_query_max_used_memory))*8,2) max_memory_grant_kb,
		ROUND(CONVERT(float, SUM(rs.avg_physical_io_reads*rs.count_executions))*8,2) total_physical_io_reads_kb,
		ROUND(CONVERT(float, SUM(rs.avg_physical_io_reads*rs.count_executions))/NULLIF(SUM(rs.count_executions), 0)*8,2) avg_physical_io_reads_kb,
		COUNT(distinct p.plan_id) num_plans
FROM sys.query_store_runtime_stats rs
JOIN sys.query_store_plan p ON p.plan_id = rs.plan_id
JOIN sys.query_store_query q ON q.query_id = p.query_id
JOIN sys.query_store_query_text qt ON q.query_text_id = qt.query_text_id
WHERE NOT (rs.first_execution_time > @interval_end_time OR rs.last_execution_time < @interval_start_time)
GROUP BY p.query_id, qt.query_sql_text, q.object_id
HAVING COUNT(distinct p.plan_id) >= 1
ORDER BY ',@sort_column,' DESC')

EXEC sp_executesql @SQL,N'@Top INT,@interval_start_time DATETIMEOFFSET(7),@interval_end_time DATETIMEOFFSET(7)',@Top,@interval_start_time,@interval_end_time