export {};

declare global {
  interface Window {
    readonly taskManagerWindow?: Readonly<{
      minimize: () => Promise<void>;
      maximize: () => Promise<void>;
      close: () => Promise<void>;
    }>;
  }
}
