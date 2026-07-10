-- 時間模擬點位餵值（由 Windows 排程 ScadaTimeSimUpdate 每分鐘執行）
-- S1 = MinuteOfDay 當日分鐘數 0–1439；S2 = HourOfDay 當日小時數 0–23，皆跨日自動歸零
-- 以 Name+Sequence JOIN 定位，不寫死 DB{n}-S{m}（CoordinatorId 由 IDENTITY 配發，換環境會變）
SET NOCOUNT ON;
UPDATE d
SET d.Value = CASE p.Sequence
                WHEN 1 THEN DATEDIFF(MINUTE, CONVERT(date, GETDATE()), GETDATE())
                WHEN 2 THEN DATEPART(HOUR, GETDATE())
              END,
    d.[Timestamp] = GETDATE(),
    d.Quality = 1
FROM DBLatestData d
JOIN DBPoints p ON p.SID = d.SID
JOIN DBCoordinator c ON c.Id = p.CoordinatorId
WHERE c.Name = 'TimeSim' AND p.Sequence IN (1, 2);
