import { dotnet } from './_framework/dotnet.js?rev=112-d595388eb0d6';

// Avalonia's browser adapter passes `types` to the File System Access API, but
// its fallback input picker expects an `accepts` property. Normalize both paths
// here so browsers without the native API still get extension/MIME filtering.
const nativeOpenFilePicker = globalThis.showOpenFilePicker?.bind(globalThis);
globalThis.showOpenFilePicker = async options => {
  const pickerOptions = { ...options, excludeAcceptAllOption: true };
  if (nativeOpenFilePicker) {
    try {
      return await nativeOpenFilePicker(pickerOptions);
    } catch (error) {
      if (error?.name === 'AbortError') throw error;
    }
  }

  const input = document.createElement('input');
  input.type = 'file';
  input.multiple = pickerOptions.multiple === true;
  input.accept = [...new Set((pickerOptions.types ?? []).flatMap(({ accept = {} }) =>
    Object.entries(accept).flatMap(([mimeType, extensions]) => [
      mimeType,
      ...(extensions ?? []).map(extension => extension.startsWith('*.')
        ? extension.slice(1)
        : extension.startsWith('.') ? extension : `.${extension}`)
    ])
  ))].join(',');
  input.style.position = 'fixed';
  input.style.top = '-100000px';
  input.style.left = '-100000px';
  document.body.appendChild(input);

  try {
    await new Promise(resolve => {
      input.addEventListener('change', resolve, { once: true });
      input.click();
    });
    return [...(input.files ?? [])].map(file => ({
      kind: 'file',
      name: file.name,
      queryPermission: async () => 'granted',
      requestPermission: async () => 'granted',
      getFile: async () => file
    }));
  } finally {
    input.remove();
  }
};

// Some browsers only emit `cancel`, not `change`, when a file picker is closed.
document.addEventListener('cancel', event => {
  const input = event.target;
  if (input instanceof HTMLInputElement && input.type === 'file') {
    input.dispatchEvent(new Event('change'));
  }
}, true);

const runtime = await dotnet.create();
const config = runtime.getConfig();
await runtime.runMain(config.mainAssemblyName, [globalThis.location.href]);
