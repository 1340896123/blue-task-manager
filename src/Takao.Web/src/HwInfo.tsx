import { useMemo } from 'react';
import { cssNumber, DataTable, MissingValue, type Column } from './components';
import { dateTime, matches } from './format';
import { useHwInfo } from './hooks/useHwInfo';
import { sorted, type SortValue } from './sorting';
import type { HwInfoReading, HwInfoSensor, HwInfoSummary, PagePreference } from './types';

type SensorRow = HwInfoReading & { sensor: HwInfoSensor };
const number = new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 6 });
const statuses = { available: '可用', unavailable: '暂不可用', stale: '数据已过期' };

export default function HwInfo({ summary, intervalMs, pref, patch, rowHeight }: { summary: HwInfoSummary; intervalMs: number; pref: PagePreference; patch: (patch: Partial<PagePreference>, silent?: boolean) => void; rowHeight: number }) {
  const { data, error, retry } = useHwInfo(intervalMs);
  const source = data ?? summary;
  const sort = { key: pref.sortKey, direction: pref.sortDirection };
  const rows = useMemo(() => {
    const list: SensorRow[] = [];
    for (const sensor of data?.sensors ?? []) for (const reading of sensor.readings) {
      if (matches(pref.query, 'HWiNFO 传感器', sensor.name, sensor.originalName, sensor.id, reading.label, reading.originalLabel, reading.unit, reading.id, String(reading.type))) list.push({ ...reading, sensor });
    }
    return sorted(list, sort, (row, key): SortValue => key === 'sensor' ? row.sensor.name : key === 'originalSensor' ? row.sensor.originalName : key === 'name' ? row.label : key === 'originalName' ? row.originalLabel : key === 'identity' ? row.id : key === 'unit' ? row.unit : key === 'value' || key === 'minimum' || key === 'maximum' || key === 'average' || key === 'type' ? row[key] : row.label);
  }, [data, pref.query, sort.key, sort.direction]);
  const metric = (row: SensorRow, field: 'value' | 'minimum' | 'maximum' | 'average') => {
    const value = row[field];
    if (field === 'value' && error) return <MissingValue reason={`读取失败，当前值无法确认：${error}`}/>;
    return value === null || !Number.isFinite(value) ? <MissingValue reason={row.unavailable[field]?.message ?? source.unavailable.source?.message ?? 'HWiNFO 未提供此数值'}/> : number.format(value);
  };
  const textWidth = cssNumber('--width-column-text', 180), nameWidth = cssNumber('--width-column-name-detail', 240), numericWidth = cssNumber('--width-column-number', 112);
  const columns: Column<SensorRow>[] = [
    { key: 'sensor', label: '传感器分组 / 用户名称', width: textWidth, render: row => <span title={`分组 ${row.sensor.id}`}>{row.sensor.name}</span> },
    { key: 'name', label: '读取项 / 用户名称', width: nameWidth, render: row => <span title={row.label}>{row.label}</span> },
    { key: 'value', label: '当前', width: numericWidth, numeric: true, render: row => metric(row, 'value') },
    { key: 'unit', label: '单位', width: numericWidth, render: row => row.unit || <span title="HWiNFO 来源的单位字段为空">无单位</span> },
    { key: 'minimum', label: '会话最小', title: 'HWiNFO 监控会话最小值', width: numericWidth, numeric: true, render: row => metric(row, 'minimum') },
    { key: 'maximum', label: '会话最大', title: 'HWiNFO 监控会话最大值', width: numericWidth, numeric: true, render: row => metric(row, 'maximum') },
    { key: 'average', label: '会话平均', title: 'HWiNFO 监控会话平均值', width: numericWidth, numeric: true, render: row => metric(row, 'average') },
    { key: 'originalSensor', label: '分组原名', width: nameWidth, render: row => <span title={row.sensor.originalName}>{row.sensor.originalName}</span> },
    { key: 'originalName', label: '读取项原名', width: nameWidth, render: row => <span title={row.originalLabel}>{row.originalLabel}</span> },
    { key: 'type', label: '原始类型', width: numericWidth, numeric: true, title: 'HWiNFO 原始 type，保留未知类型编号', render: row => row.type },
    { key: 'identity', label: '来源身份', width: textWidth, title: 'sensorId:sensorInstance:readingId', render: row => <span title={row.id}>{row.id}</span> },
  ];
  return <>
    <div className="hwinfo-source">
      <p><strong>{statuses[source.status]}</strong> · {source.sensorCount} 个分组 / {source.readingCount} 个读取项{data && ` · 搜索结果 ${rows.length} 项`}</p>
      <p>HWiNFO 来源时刻：{source.pollTimeUtc ? dateTime(source.pollTimeUtc) : '尚未读取'} · 本应用读取：{source.capturedAtUtc ? dateTime(source.capturedAtUtc) : '尚未完成'}</p>
      <p>最小 / 最大 / 平均来自 HWiNFO 监控会话；不属于本应用历史。单位与名称保持来源内容。</p>
      {Object.values(source.unavailable).map((reason, index) => <p className="metric-reasons" key={`${reason.code}:${index}`} role="status">{reason.message}</p>)}
      {error && <p className="form-error" role="alert">{error} 当前值暂不显示；保留的会话统计来自上次读取。</p>}
      {(source.status !== 'available' || error) && <p>请在已有 HWiNFO 的 Sensors 设置中确认 Shared Memory Support（共享内存支持）已开启，并确认当前版本 / 许可提供此功能。未运行、共享内存未开启或当前版本 / 许可不提供时均可能不可用。</p>}
      <button className="text-button" onClick={retry}>重新读取 HWiNFO</button>
    </div>
    <DataTable rows={rows} columns={columns} rowKey={row => row.id} label="HWiNFO 传感器" rowHeight={rowHeight} sort={sort}
      onSort={key => patch({ sortKey: key, sortDirection: pref.sortKey === key && pref.sortDirection === 'ascending' ? 'descending' : 'ascending', scrollTop: 0 })}
      columnWidths={pref.columnWidths} onColumnWidthsChange={columnWidths => patch({ columnWidths })}
      initialScrollTop={pref.scrollTop} onScrollTop={scrollTop => patch({ scrollTop }, true)} scrollResetKey={`${pref.query}:${pref.sortKey}:${pref.sortDirection}`}
      emptyTitle={!data && !error ? '正在读取 HWiNFO 共享内存' : data?.readingCount ? '没有匹配的传感器读取项' : source.status === 'available' ? 'HWiNFO 尚未发布传感器读取项' : 'HWiNFO 传感器暂不可用'}
      emptyDescription={data?.sensors.length ? '请修改或清空顶部共用搜索。' : '当前只展示真实来源；请按上方原因与共享内存指引检查 HWiNFO。'}/>
  </>;
}
