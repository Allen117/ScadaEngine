@echo off
cd /d "C:\Users\A50388.ITRI\Desktop\ScadaEngine\ScadaEngine.Web"
echo 正在啟動 SCADA Login 網頁...
start http://localhost:5038
dotnet run
pause