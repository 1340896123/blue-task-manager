export type Missing = { code: string; message: string; nativeErrorCode?: number | null };
export type Availability = Record<string, Missing>;
export type Capability = { available: boolean; source: string; reason: string | null };
export type Settings = { intervalMs: number; paused: boolean };
export type Bootstrap = { token: string; settings: Settings; capabilities: Record<string, Capability>; version: string; desktop: boolean };
export type Process = {
  identity: string; pid: number; parentPid: number; creationTimeFileTime: string | null; name: string;
  path: string | null; publisher: string | null; sessionId: number | null; userName: string | null;
  threadCount: number; priority: string | null; cpuPercent: number | null; workingSetBytes: number | null;
  ioReadBytesPerSecond: number | null; ioWriteBytesPerSecond: number | null;
  totalCpuTime100ns: string | null; ioReadBytes: string | null; ioWriteBytes: string | null;
  networkBytesPerSecond: number | null; gpuPercent: number | null; canTerminate: boolean; state: string; unavailable: Availability;
};
export type SystemInfo = {
  cpuPercent: number | null; memoryPercent: number | null; totalPhysicalBytes: number | null;
  availablePhysicalBytes: number | null; usedPhysicalBytes: number | null;
  networkReceiveBytesPerSecond: number | null; networkSendBytesPerSecond: number | null;
  networkInterfaceCount: number; processCount: number; logicalProcessorCount: number; uptimeSeconds: number; unavailable: Availability;
};
export type HistoryPoint = { capturedAtUtc: string; cpuPercent: number | null; memoryPercent: number | null; networkReceiveBytesPerSecond: number | null; networkSendBytesPerSecond: number | null };
export type ProcessHistory = { identity: string; pid: number; name: string; firstObservedAtUtc: string; lastObservedAtUtc: string; isRunning: boolean; observedCpuTime100ns: string | null; observedIoReadBytes: string | null; observedIoWriteBytes: string | null };
export type Service = { name: string; displayName: string; state: string; pid: number };
export type StartupItem = { id: string; name: string; command: string; source: string; enabled: string | null; enabledReason: string };
export type SessionUser = { sessionId: number; userName: string | null; stationName: string; state: string; processCount: number; cpuPercent: number | null; workingSetBytes: number | null; unreadableProcessCount: number; unavailable: Availability };
export type Snapshot = {
  sampleId: number; capturedAtUtc: string; sessionStartedAtUtc: string; intervalMs: number; paused: boolean; system: SystemInfo;
  processes: Process[]; services: Service[]; startupItems: StartupItem[]; users: SessionUser[]; history: ProcessHistory[];
  performanceHistory: HistoryPoint[]; capabilities: Record<string, Capability>; warnings: Missing[]; slowDataCapturedAtUtc: string | null;
};
export type ProcessResult = { pid: number; identity: string | null; creationTimeFileTime: string | null; message: string };
