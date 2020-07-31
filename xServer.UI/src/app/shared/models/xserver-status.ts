export class XServerStatus {
  constructor(connected: number) {
    this.connected = connected;
  }

  public connected: number;
  public nodes: xServerPeer[];
}

class xServerPeer {
  public name: string;

  public networkProtocol: number;

  public networkAddress: string;

  public priority: number;

  public networkPort: number;

  public version: string;

  public responseTime: number;

  public tier: number;
}
