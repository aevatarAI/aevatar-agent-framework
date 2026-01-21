// ------------------------------------------------------------
//  Providers API
//  说明：
//  - 获取 / 切换默认 provider
// ------------------------------------------------------------
export type ProviderEntry = {
  name: string;
  defaultModel: string;
};

export type ProvidersSnapshot = {
  defaultProvider: string;
  defaultModel: string;
  providers: ProviderEntry[];
};

export type ProvidersApi = {
  list: () => Promise<ProvidersSnapshot | null>;
  setDefault: (provider: string) => Promise<ProvidersSnapshot | null>;
};

export function createProvidersApi(backendUrl: string): ProvidersApi {
  async function list() {
    if (!backendUrl) return null;
    try {
      const res = await fetch(`${backendUrl}/api/providers`);
      if (!res.ok) return null;
      return (await res.json()) as ProvidersSnapshot;
    } catch {
      return null;
    }
  }

  async function setDefault(provider: string) {
    if (!backendUrl) return null;
    try {
      const res = await fetch(`${backendUrl}/api/providers/default`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ provider }),
      });
      if (!res.ok) return null;
      return (await res.json()) as ProvidersSnapshot;
    } catch {
      return null;
    }
  }

  return { list, setDefault };
}
