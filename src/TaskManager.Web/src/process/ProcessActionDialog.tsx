import { useEffect, useState } from 'react';
import { api, errorMessage, identityRoute } from '../api';
import { Modal } from '../components';
import { dateTime } from '../format';
import type { EfficiencyState, Priority, PriorityResult, Process, ProcessResult, TreePreview, TreeResult } from '../types';
export const priorityLabels: Record<Priority, string> = { idle: '空闲', belowNormal: '低于正常', normal: '正常', aboveNormal: '高于正常', high: '高' };
export default function ProcessActionDialog({ action, target, alive, connected, onClose, onChanged }: { action: string; target: Process; alive: boolean; connected: boolean; onClose: () => void; onChanged: () => void }) {
  const [busy, setBusy] = useState(false); const [reading, setReading] = useState(action === 'tree' || action === 'efficiency');
  const [error, setError] = useState<string | null>(null); const [result, setResult] = useState<string | null>(null);
  const [priority, setPriority] = useState<Priority>((Object.keys(priorityLabels) as Priority[]).find(key => priorityLabels[key] === target.priority) ?? 'normal');
  const [tree, setTree] = useState<TreePreview | null>(null); const [treeResult, setTreeResult] = useState<TreeResult | null>(null); const [efficiency, setEfficiency] = useState<EfficiencyState | null>(null);
  const [readRevision, setReadRevision] = useState(0);
  useEffect(() => {
    if (!['tree', 'efficiency'].includes(action)) return;
    let active = true; const controller = new AbortController(); setReading(true);
    const read = action === 'tree' ? api.post<TreePreview>(`processes/${target.pid}/tree-preview`, { creationTimeFileTime: target.creationTimeFileTime }, controller.signal) : api.get<EfficiencyState>(identityRoute(target, 'efficiency'), controller.signal);
    void read.then(value => { if (active) { if (action === 'tree') setTree(value as TreePreview); else setEfficiency(value as EfficiencyState); } }).catch(caught => { if (active) setError(errorMessage(caught)); }).finally(() => { if (active) setReading(false); });
    return () => { active = false; controller.abort(); };
  }, [target.identity, action, readRevision]);
  const submit = async () => {
    if (busy || !alive || !connected || !target.creationTimeFileTime) return;
    setBusy(true); setError(null);
    try {
      const body = { creationTimeFileTime: target.creationTimeFileTime };
      if (action === 'terminate') {
        const response = await api.post<ProcessResult>(`processes/${target.pid}/terminate`, body);
        setResult(`${response.completed ? '已确认退出' : '尚未确认退出'}：${response.message}`);
      } else if (action === 'priority') {
        const response = await api.post<PriorityResult>(`processes/${target.pid}/priority`, { ...body, priority });
        setResult(`Windows 实际读回优先级：${priorityLabels[response.priority]}。${response.message}`);
      } else if (action === 'efficiency' && efficiency) {
        const response = await api.post<EfficiencyState>(`processes/${target.pid}/efficiency`, { ...body, enabled: !efficiency.canRestore && !efficiency.enabled });
        setEfficiency(response); setResult(`Windows 实际读回：${response.enabled ? '效率模式已启用' : '原优先级与电源节流状态已恢复'}。优先级 ${response.priority ?? '不可读取'}`);
      } else if (action === 'tree' && tree) {
        const response = await api.post<TreeResult>(`processes/${target.pid}/terminate-tree`, { ...body, members: tree.members.map(member => ({ pid: member.pid, creationTimeFileTime: member.creationTimeFileTime })) });
        setTreeResult(response); setResult(response.message);
      }
      onChanged();
    } catch (caught) { setError(errorMessage(caught)); onChanged(); }
    finally { setBusy(false); }
  };
  const title = action === 'terminate' ? '结束此进程？' : action === 'tree' ? '结束进程树？' : action === 'priority' ? '设置进程优先级' : 'Windows 效率模式';
  const blocked = action === 'tree' ? !tree?.canTerminate : action === 'efficiency' ? !efficiency?.available || (efficiency.enabled && !efficiency.canRestore) : false;
  return <Modal title={title} wide={action === 'tree'} busy={busy} onClose={onClose} description={`${target.name} · PID ${target.pid}。目标身份在打开对话框时固定。`}>
    {!alive && <p className="form-error">目标已退出或已不在最新快照中；所有写操作已停用。</p>}
    {action === 'terminate' && <p className="impact-note">未保存的工作可能会丢失。仅结束这一条进程，不会自动结束子进程。</p>}
    {action === 'priority' && <><p className="impact-note">当前快照优先级：{target.priority ?? '不可读取'}。较高优先级可能影响系统响应；优先级会持续作用于Windows目标，关闭本应用不会恢复。</p><label className="field-label" htmlFor="priority-choice">目标优先级</label><select id="priority-choice" className="form-input" value={priority} onChange={event => setPriority(event.target.value as Priority)} disabled={busy || Boolean(result)}>{(Object.keys(priorityLabels) as Priority[]).map(key => <option value={key} key={key}>{priorityLabels[key]}</option>)}</select></>}
    {reading && <p className="impact-note">正在向 Windows 核查目标…</p>}
    {action === 'efficiency' && efficiency && <><dl className="process-details"><div><dt>实际状态</dt><dd>{efficiency.enabled ? '已启用' : '未启用'}</dd></div><div><dt>实际优先级</dt><dd>{efficiency.priority ?? '不可读取'}</dd></div><div><dt>可恢复本次原状态</dt><dd>{efficiency.canRestore ? '是' : '否'}</dd></div></dl>{efficiency.reason && <p className="impact-note">{efficiency.reason}</p>}<p className="impact-note">启用会设置Windows执行速度节流与空闲优先级，可能降低响应速度。原始设置仅在本次应用运行期间保留；关闭本应用不会自动恢复目标，重新启动后无法恢复这份原始设置。恢复前不能交叉修改普通优先级。</p></>}
    {action === 'tree' && tree && <><p className="impact-note">{tree.message} · 共 {tree.members.length} 个成员 · 预览 {dateTime(tree.capturedAtUtc)}。未保存工作可能丢失；提交前重验完整成员集合，变化时拒绝。执行期间仍可能出现新子进程，请在操作后检查实际状态。</p><div className="tree-preview" role="list" aria-label="结束树完整成员">{tree.members.map(member => <div key={member.identity} role="listitem"><strong>{member.name}</strong><span>PID {member.pid}</span><span>{member.canTerminate ? '可结束' : member.reason ?? '不可结束'}</span></div>)}</div></>}
    {error && <div className="form-error" role="alert">{error}</div>}{['tree', 'efficiency'].includes(action) && !result && <button className="command-button bordered" disabled={busy || reading || !alive || !connected} onClick={() => { setError(null); setReadRevision(v => v + 1); }}>{action === 'tree' ? '重新预览完整成员' : '刷新实际效率状态'}</button>}{result && <div className="operation-result" role="status">{result}</div>}
    {treeResult && <div className="tree-preview" aria-label="树结束逐项结果">{treeResult.results.map(item => <div key={item.identity}><strong>PID {item.pid}</strong><span>{({ terminated: '已确认退出', failed: '失败', pending: '待确认' })[item.status]}</span><span>{item.message}{item.nativeErrorCode != null ? `（Windows ${item.nativeErrorCode}）` : ''}</span></div>)}</div>}
    <div className="modal-actions"><button className="command-button bordered" disabled={busy} onClick={onClose}>{result ? '关闭' : '取消'}</button>{!result && <button className={`command-button ${['terminate', 'tree'].includes(action) ? 'danger' : 'primary'}`} disabled={busy || reading || blocked || !alive || !connected} onClick={() => void submit()}>{busy ? '正在处理…' : action === 'terminate' ? '结束进程' : action === 'tree' ? '结束完整进程树' : action === 'priority' ? '确认变更' : efficiency?.canRestore ? '恢复原状态' : '启用效率模式'}</button>}</div>
  </Modal>;
}
