import { dotnet } from './_framework/dotnet.js';

// Avalonia's file-input picker waits for a `change` event, which browsers do not
// emit when the user cancels the picker. Resolve that pending operation so the
// editor can open another picker on the next cell click.
document.addEventListener('cancel', event => {
  const input = event.target;
  if (input instanceof HTMLInputElement && input.type === 'file') {
    input.dispatchEvent(new Event('change'));
  }
}, true);

const runtime = await dotnet.create();
const config = runtime.getConfig();
await runtime.runMain(config.mainAssemblyName, [globalThis.location.href]);
