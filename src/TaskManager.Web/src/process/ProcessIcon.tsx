import ApplicationIcon from '../ApplicationIcon';
import type { Process } from '../types';
export default function ProcessIcon({ process }: { process: Process }) {
  return <ApplicationIcon kind="process" id={process.identity} revision={process.path ?? ''}/>;
}
