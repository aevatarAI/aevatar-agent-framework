export type ProviderItem = {
  id: string;
  displayName: string;
  category: "configured" | "popular" | "other" | string;
  description?: string;
  recommended?: boolean;
  connected?: boolean;
};

export type ProviderPublic = {
  providerName: string;
  displayName: string;
  kind: string;
  apiKeyConfigured: boolean;
  endpoint: string;
  endpointSource: string; // secret | default | missing
  model: string;
  modelSource: string; // secret | default | missing
};

export type View = "list" | "connect" | "advanced";


