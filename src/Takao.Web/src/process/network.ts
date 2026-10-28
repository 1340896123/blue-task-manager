import { dateTime, rate, reason } from '../format';
import type { Process, ProcessNetworkStatus } from '../types';

export const networkScope = 'Windows ETW TCP/UDP IPv4/IPv6；含系统报告的回环和虚拟网络，与硬件网卡总量口径不同。';
export function networkDescription(process: Process) {
  const field = (key: 'networkReceiveBytesPerSecond' | 'networkSendBytesPerSecond') => process[key] === null ? `—（${reason(process.unavailable, key)}）` : rate(process[key]);
  return `接收 ${field('networkReceiveBytesPerSecond')}；发送 ${field('networkSendBytesPerSecond')}；来源 ${process.networkCapturedAtUtc ?? '尚无有效网络窗口'}。`;
}
export function networkState(status: ProcessNetworkStatus, paused: boolean) {
  if (paused) return '采集已暂停';
  return ({ available: '正常', warmingUp: '正在预热', paused: '采集已暂停', stale: '数据已过期', accessDenied: '访问被拒绝', notSupported: '当前不可用', eventsLost: '采样不完整', capacity: '超出采集容量', error: '采集失败' } as Record<string, string>)[status.status] ?? '当前不可用';
}
export function networkSourceText(status: ProcessNetworkStatus, paused: boolean) {
  const messages = [...new Set(Object.values(status.unavailable).map(item => item.message))];
  return `${networkState(status, paused)} · ${status.capturedAtUtc ? `来源 ${dateTime(status.capturedAtUtc)}` : '等待首个完整窗口'}${status.windowDurationMs === null ? '' : ` · 观测窗口 ${(status.windowDurationMs / 1000).toFixed(2)} 秒`}${messages.length ? ` · ${messages.join('；')}` : ''}${status.eventsLost || status.buffersLost ? ` · 丢失事件 ${status.eventsLost} / 缓冲 ${status.buffersLost}` : ''}`;
}
