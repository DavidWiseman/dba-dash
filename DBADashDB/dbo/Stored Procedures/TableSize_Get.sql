CREATE PROC dbo.TableSize_Get(
	@InstanceIDs IDs READONLY,
	@GrowthDays INT=30,
	@Top INT = 1000,
	@DatabaseID INT
)
AS
SET  NOCOUNT ON
DECLARE @MinSnapshotDate DATETIME2
SELECT @MinSnapshotDate = DATEADD(d,-@GrowthDays,SYSUTCDATETIME());

CREATE TABLE #LatestSnapshots(
	InstanceID INT PRIMARY KEY,
	SnapshotDate DATETIME2 NOT NULL
)
CREATE TABLE #Latest (
    ObjectID BIGINT PRIMARY KEY,
    InstanceID INT NOT NULL,
    DatabaseID INT NOT NULL,
    Instance NVARCHAR(128) NOT NULL,
    SnapshotDate DATETIME2(7) NOT NULL,
    DB NVARCHAR(128) NOT NULL,
    SchemaName NVARCHAR(128) NOT NULL,
    ObjectName NVARCHAR(128) NOT NULL,
    Rows BIGINT NOT NULL,
    Reserved_KB BIGINT NOT NULL,
    Used_KB BIGINT NOT NULL,
    Data_KB BIGINT NOT NULL,
    Index_KB BIGINT NULL
);

INSERT INTO #LatestSnapshots(
		InstanceID,
		SnapshotDate
)
SELECT T.ID,
		Latest.SnapshotDate
FROM @InstanceIDs T 
CROSS APPLY(	
				SELECT TOP(1) SnapshotDate 
				FROM dbo.TableSize TS 
				WHERE TS.InstanceID = T.ID
				ORDER BY TS.SnapshotDate DESC
			) Latest

INSERT INTO #Latest(
	ObjectID,
	InstanceID,
	DatabaseID,
	Instance,
	SnapshotDate,
	DB,
	SchemaName,
	ObjectName,
	Rows,
	Reserved_KB,
	Used_KB,
	Data_KB,
	Index_KB
)
SELECT TOP(@Top) TS.ObjectID,
				TS.InstanceID,
				TS.DatabaseID,
				I.InstanceDisplayName AS Instance,
				TS.SnapshotDate,
				D.name AS DB,
				O.SchemaName,
				O.ObjectName,
				TS.row_count AS Rows,
				TS.reserved_pages*8 as Reserved_KB,
				TS.used_pages*8 AS Used_KB,
				TS.data_pages*8 AS Data_KB,
				TS.index_pages*8 AS Index_KB
FROM #LatestSnapshots T
JOIN dbo.TableSize TS ON TS.InstanceID = T.InstanceID AND TS.SnapshotDate = T.SnapshotDate
JOIN dbo.DBObjects O ON TS.ObjectID = O.ObjectID AND O.DatabaseID = TS.DatabaseID 
JOIN dbo.Databases D ON O.DatabaseID = D.DatabaseID AND D.InstanceID = T.InstanceID
JOIN dbo.Instances I ON T.InstanceID = I.InstanceID
WHERE D.IsActive=1
AND I.IsActive=1
AND O.IsActive=1
AND (D.DatabaseID = @DatabaseID OR @DatabaseID IS NULL)
ORDER BY TS.reserved_pages DESC
OPTION(FORCE ORDER,OPTIMIZE FOR(@Top=100)) /* Top is typically 1000, but we want to influence the query optimizer to sort early in the plan  */

SELECT L.ObjectID,
	L.InstanceID,
	L.Instance,
	L.[SnapshotDate],
	ISNULL(SSD.Status,3) AS SnapshotStatus,
	L.DB,
	L.SchemaName,
	L.ObjectName,
	L.Rows,
	L.Reserved_KB,
	L.Used_KB,
	L.Data_KB,
	L.Index_KB,
	(L.Rows- Oldest.Rows)*1440.0 / NULLIF(DATEDIFF(mi,Oldest.SnapshotDate,L.SnapshotDate),0) AS Avg_Rows_Per_Day,
	(L.Used_KB - Oldest.Used_KB)*1440.0 / NULLIF(DATEDIFF(mi,Oldest.SnapshotDate,L.SnapshotDate),0) AS Avg_KB_Per_Day,
	NULLIF(DATEDIFF(mi,Oldest.SnapshotDate,L.SnapshotDate),0)/1440.0 AS CalcDays	
FROM #Latest L
LEFT JOIN dbo.CollectionDatesStatus SSD ON SSD.InstanceID = L.InstanceID AND SSD.Reference='TableSize'
CROSS APPLY(SELECT TOP(1)	TS.row_count AS Rows,
							TS.used_pages*8 AS Used_KB,
							TS.SnapshotDate
			FROM dbo.TableSize TS
			WHERE TS.ObjectID = L.ObjectID
			AND TS.SnapshotDate >= @MinSnapshotDate
			ORDER BY SnapshotDate
			) Oldest
ORDER BY L.Reserved_KB DESC
