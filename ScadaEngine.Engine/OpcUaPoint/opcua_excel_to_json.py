# -*- coding: utf-8 -*-
"""
OPC UA 通訊檔案產生工具（Excel → OpcUaPoint JSON 聚集）

用法：
    python opcua_excel_to_json.py [xlsm路徑] [--live]

    不加 --live 時輸出 {SheetName}.json.example（Engine 不載入，安全預覽）
    加 --live 時輸出 {SheetName}.json（Engine 下次 reload/重啟即載入）

Excel 格式（每個 sheet = 一個 OPC UA Server）：
    第 1 列：連線設定 key-value 對，例
        EndpointUrl | opc.tcp://... | Username | u | Password | p | ConnectTimeout | 5000 | PollingInterval | 1000
    第 2 列：欄位標題（名稱 TagName ControlType Ratio 單位 最小值 最大值 Device）
    第 3 列起：點位資料

Device 分層（自動）優先序：
    1. H 欄「Device」有填 → 用填的值
    2. TagName 可解析 → 取 s= 識別碼第一個 '.' 前綴（ns=2;s=D1.T → D1）
    3. 都沒有 → 歸入 sheet 名那組

Seq 規則：依列序 1~N 連續配號，NextSeq = N+1（首次產出用；
    檔案上線後若要增刪點位，請改用 Web「OPC UA 來源」頁 — Web 會依 NextSeq
    配新號且刪除不回收，確保既有 SID 不漂移。用 Excel 重轉會整批重編 Seq）
"""
import json
import math
import re
import sys

import pandas as pd

DEFAULT_SRC = 'OPCUA通訊檔案產生工具.xlsm'


def cell_str(v, default=''):
    if v is None or (isinstance(v, float) and math.isnan(v)):
        return default
    return str(v).strip()


def cell_num(v, default):
    try:
        f = float(v)
        return int(f) if f == int(f) else f
    except (TypeError, ValueError):
        return default


def fmt_ratio(v):
    f = float(v)
    return str(int(f)) if f == int(f) else repr(f)


def device_from_tagname(tagname):
    """ns=2;s=D1.T → D1；無 s= 識別碼或無 '.' → None"""
    m = re.search(r'(?:^|;)s=(.+)$', tagname)
    if m and '.' in m.group(1):
        return m.group(1).split('.')[0].strip() or None
    return None


def convert_sheet(xl, sheet):
    df = xl.parse(sheet, header=None)

    kv = {}
    row0 = df.iloc[0].tolist()
    i = 0
    while i + 1 < len(row0):
        k = row0[i]
        if isinstance(k, str) and k.strip():
            kv[k.strip()] = row0[i + 1]
            i += 2
        else:
            i += 1

    devices = {}
    order = []
    seq = 0
    for _, row in df.iloc[2:].iterrows():
        name = cell_str(row[0])
        tagname = cell_str(row[1])
        if not name and not tagname:
            continue
        seq += 1

        ct = cell_str(row[2]).upper()
        if ct not in ('', 'AO', 'DO'):
            print(f'  [警告] {name}: ControlType "{ct}" 非法（僅限空白/AO/DO），已改為唯讀')
            ct = ''

        ratio = cell_num(row[3], 1)
        if ratio == 0:
            print(f'  [警告] {name}: Ratio=0 非法，已改為 1')
            ratio = 1

        tag = {
            'Seq': seq,
            'Name': name,
            'TagName': tagname,
            'ControlType': ct,
            'Ratio': fmt_ratio(ratio),
            'Unit': cell_str(row[4]),
        }
        mn = cell_num(row[5], None)
        mx = cell_num(row[6], None)
        if mn is not None:
            tag['Min'] = mn
        if mx is not None:
            tag['Max'] = mx

        # Device 分層：H 欄 > TagName 解析 > sheet 名
        dev = cell_str(row[7]) or device_from_tagname(tagname) or sheet
        if dev not in devices:
            devices[dev] = []
            order.append(dev)
        devices[dev].append(tag)

    return {
        'Name': sheet,
        'EndpointUrl': cell_str(kv.get('EndpointUrl')),
        'Username': cell_str(kv.get('Username')),
        'Password': cell_str(kv.get('Password')),
        'PollingInterval': int(cell_num(kv.get('PollingInterval'), 1000)),
        'ConnectTimeout': int(cell_num(kv.get('ConnectTimeout'), 5000)),
        'MonitorEnabled': True,
        'MaxNodesPerRead': 0,
        'NextSeq': seq + 1,
        'Devices': [{'Name': d, 'Tags': devices[d]} for d in order],
    }


def main():
    args = [a for a in sys.argv[1:] if a != '--live']
    is_live = '--live' in sys.argv
    src = args[0] if args else DEFAULT_SRC

    xl = pd.ExcelFile(src)
    for sheet in xl.sheet_names:
        out = convert_sheet(xl, sheet)
        ext = '.json' if is_live else '.json.example'
        dst = f'{sheet}{ext}'
        with open(dst, 'w', encoding='utf-8') as f:
            json.dump(out, f, ensure_ascii=False, indent=2)
            f.write('\n')
        n_pts = sum(len(d['Tags']) for d in out['Devices'])
        devs = ', '.join(f"{d['Name']}({len(d['Tags'])})" for d in out['Devices'])
        print(f'{dst}: {n_pts} 點 / {len(out["Devices"])} 組 Device → {devs}')


if __name__ == '__main__':
    main()
