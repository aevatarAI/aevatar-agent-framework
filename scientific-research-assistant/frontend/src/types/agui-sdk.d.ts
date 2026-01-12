declare module "@agui/sdk" {
  // Minimal type surface used by this repo.
  export class AgUiClient {
    constructor(url: string, options?: any);
    on(type: string, handler: (event: any) => void): void;
    close(): void;
  }
}


