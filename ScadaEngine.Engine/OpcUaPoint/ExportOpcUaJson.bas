Attribute VB_Name = "OpcUaExport"
Option Explicit

' 專為 iAUTO SCADA 專案開發的 OPC UA JSON 匯出工具
' 一個 sheet = 一個 OPC UA Server（輸出檔名 = sheet 名 .json）
'
' Excel 格式：
'   第 1 列：連線設定 key-value 對（EndpointUrl | url | Username | u | Password | p | ConnectTimeout | ms | PollingInterval | ms）
'   第 2 列：欄位標題
'   第 3 列起：A=名稱 B=TagName(NodeId) C=ControlType(空/AO/DO) D=Ratio E=單位 F=Min G=Max H=Device
'
' Device 分層優先序：H 欄 > TagName「s=」識別碼第一個「.」前綴（ns=2;s=D1.T → D1）> sheet 名
' Excel 沒有的項目用系統預設：MonitorEnabled=true、MaxNodesPerRead=0、NextSeq=點數+1
' 注意：Ratio 輸出字串（比照 Modbus 慣例）；Min/Max 輸出數字、空白則省略（Engine 端為 float?）

Private Function JsonEsc(ByVal s As String) As String
    s = Replace(s, "\", "\\")
    s = Replace(s, """", "\""")
    JsonEsc = s
End Function

' Str() 恆用小數點，避開地區設定造成 JSON 數字格式錯誤
Private Function NumStr(ByVal v As Variant) As String
    NumStr = Trim(Str(CDbl(v)))
End Function

Private Function KvGet(ByVal kv As Object, ByVal k As String, ByVal dflt As String) As String
    If kv.Exists(k) Then
        KvGet = Trim(CStr(kv(k)))
        If KvGet = "" Then KvGet = dflt
    Else
        KvGet = dflt
    End If
End Function

Sub ExportOpcUaJson()
    Dim ws As Worksheet
    Set ws = ActiveSheet

    ' --- 1. 讀第 1 列 key-value 連線設定 ---
    Dim kv As Object
    Set kv = CreateObject("Scripting.Dictionary")
    Dim c As Long
    c = 1
    Do While c < 40
        Dim k As String
        k = Trim(CStr(ws.Cells(1, c).Value))
        If k <> "" Then
            kv(k) = Trim(ws.Cells(1, c + 1).Text)
            c = c + 2
        Else
            c = c + 1
        End If
    Loop

    If KvGet(kv, "EndpointUrl", "") = "" Then
        MsgBox "【格式錯誤】第 1 列需有 EndpointUrl（例 A1=EndpointUrl, B1=opc.tcp://...）", vbCritical
        Exit Sub
    End If

    ' --- 2. 逐列讀點位，依 Device 分組（保持出現順序） ---
    Dim lastRow As Long
    lastRow = ws.Cells(ws.Rows.Count, "A").End(xlUp).Row
    If lastRow < 3 Then
        MsgBox "【中止】從第 3 列起找不到任何點位資料。", vbExclamation
        Exit Sub
    End If

    Dim devOrder As New Collection
    Dim devTags As Object
    Set devTags = CreateObject("Scripting.Dictionary")
    Dim seq As Long
    seq = 0

    Dim i As Long
    For i = 3 To lastRow
        Dim nm As String, tagName As String, ct As String, ratio As String, dev As String, t As String
        nm = Trim(CStr(ws.Cells(i, 1).Value))
        tagName = Trim(CStr(ws.Cells(i, 2).Value))
        If nm = "" And tagName = "" Then GoTo NextRow
        seq = seq + 1

        ' ControlType 僅限 空/AO/DO（寫入型別由 Server 節點自帶，Engine 自動轉換）
        ct = UCase(Trim(CStr(ws.Cells(i, 3).Value)))
        If ct <> "AO" And ct <> "DO" Then ct = ""

        ' 讀 .Value 再正規化，避免儲存格顯示格式（如 1.00）滲入輸出
        If Trim(ws.Cells(i, 4).Text) = "" Or Val(CStr(ws.Cells(i, 4).Value)) = 0 Then
            ratio = "1"
        Else
            ratio = NumStr(ws.Cells(i, 4).Value)
        End If

        ' Device：H 欄 > TagName「s=」識別碼第一個「.」前綴 > sheet 名
        dev = Trim(CStr(ws.Cells(i, 8).Value))
        If dev = "" Then
            Dim p As Long, ident As String
            ident = ""
            p = InStr(tagName, ";s=")
            If p > 0 Then
                ident = Mid(tagName, p + 3)
            ElseIf Left(tagName, 2) = "s=" Then
                ident = Mid(tagName, 3)
            End If
            If InStr(ident, ".") > 0 Then dev = Left(ident, InStr(ident, ".") - 1)
        End If
        If dev = "" Then dev = ws.Name

        t = "        {" & vbCrLf
        t = t & "          ""Seq"": " & seq & "," & vbCrLf
        t = t & "          ""Name"": """ & JsonEsc(nm) & """," & vbCrLf
        t = t & "          ""TagName"": """ & JsonEsc(tagName) & """," & vbCrLf
        t = t & "          ""ControlType"": """ & ct & """," & vbCrLf
        t = t & "          ""Ratio"": """ & JsonEsc(ratio) & """," & vbCrLf
        t = t & "          ""Unit"": """ & JsonEsc(Trim(CStr(ws.Cells(i, 5).Value))) & """"
        If Trim(ws.Cells(i, 6).Text) <> "" Then t = t & "," & vbCrLf & "          ""Min"": " & NumStr(ws.Cells(i, 6).Value)
        If Trim(ws.Cells(i, 7).Text) <> "" Then t = t & "," & vbCrLf & "          ""Max"": " & NumStr(ws.Cells(i, 7).Value)
        t = t & vbCrLf & "        }"

        If Not devTags.Exists(dev) Then
            devTags.Add dev, New Collection
            devOrder.Add dev
        End If
        devTags(dev).Add t
NextRow:
    Next i

    ' --- 3. 組整體 JSON（Excel 沒有的項目用預設值） ---
    Dim json As String
    json = "{" & vbCrLf
    json = json & "  ""Name"": """ & JsonEsc(ws.Name) & """," & vbCrLf
    json = json & "  ""EndpointUrl"": """ & JsonEsc(KvGet(kv, "EndpointUrl", "")) & """," & vbCrLf
    json = json & "  ""Username"": """ & JsonEsc(KvGet(kv, "Username", "")) & """," & vbCrLf
    json = json & "  ""Password"": """ & JsonEsc(KvGet(kv, "Password", "")) & """," & vbCrLf
    json = json & "  ""PollingInterval"": " & KvGet(kv, "PollingInterval", "1000") & "," & vbCrLf
    json = json & "  ""ConnectTimeout"": " & KvGet(kv, "ConnectTimeout", "5000") & "," & vbCrLf
    json = json & "  ""MonitorEnabled"": true," & vbCrLf
    json = json & "  ""MaxNodesPerRead"": 0," & vbCrLf
    json = json & "  ""NextSeq"": " & (seq + 1) & "," & vbCrLf
    json = json & "  ""Devices"": [" & vbCrLf

    Dim d As Long, j As Long
    For d = 1 To devOrder.Count
        Dim tags As Collection
        Set tags = devTags(devOrder(d))
        json = json & "    {" & vbCrLf
        json = json & "      ""Name"": """ & JsonEsc(CStr(devOrder(d))) & """," & vbCrLf
        json = json & "      ""Tags"": [" & vbCrLf
        For j = 1 To tags.Count
            json = json & tags(j)
            If j < tags.Count Then json = json & ","
            json = json & vbCrLf
        Next j
        json = json & "      ]" & vbCrLf & "    }"
        If d < devOrder.Count Then json = json & ","
        json = json & vbCrLf
    Next d
    json = json & "  ]" & vbCrLf & "}" & vbCrLf

    ' --- 4. UTF-8 輸出（Engine loader 讀 UTF-8） ---
    On Error GoTo FileError
    Dim filePath As String
    filePath = ThisWorkbook.Path & "\" & ws.Name & ".json"
    Dim st As Object
    Set st = CreateObject("ADODB.Stream")
    st.Type = 2
    st.Charset = "utf-8"
    st.Open
    st.WriteText json
    st.SaveToFile filePath, 2
    st.Close

    MsgBox "【產生成功】" & ws.Name & ".json" & vbCrLf & _
           "點位 " & seq & " 個 / Device " & devOrder.Count & " 組" & vbCrLf & _
           "Engine 於下次 reload / 重啟時載入。", vbInformation
    Exit Sub

FileError:
    MsgBox "【檔案寫入失敗】" & Err.Description, vbCritical
End Sub
