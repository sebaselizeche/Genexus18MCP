import * as http from "http";
import { GxShadowService } from "../gxShadowService";

export class GxGatewayClient {
  private _baseUrl = "http://localhost:5000/api/command";
  private _shadowService?: GxShadowService;

  constructor(baseUrl: string, shadowService?: GxShadowService) {
    this._baseUrl = baseUrl;
    this._shadowService = shadowService;
  }

  public get baseUrl(): string {
    return this._baseUrl;
  }

  set baseUrl(url: string) {
    this._baseUrl = url;
  }

  async call(command: any, customTimeout?: number): Promise<any> {
    return new Promise((resolve, reject) => {
      if (this._shadowService && command.params) {
        command.params.shadowPath = this._shadowService.shadowRoot;
      }

      const data = JSON.stringify(command);
      const timeout = customTimeout || 60000;
      
      const req = http.request(
        this._baseUrl,
        {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
            "Content-Length": Buffer.byteLength(data),
          },
          timeout: timeout,
        },
        (res) => {
          let body = "";
          res.on("data", (chunk) => (body += chunk));
          res.on("end", () => {
            try {
              resolve(JSON.parse(body));
            } catch {
              resolve(body);
            }
          });
        }
      );

      req.on("timeout", () => {
        req.destroy();
        reject(new Error(`Timeout Gateway (${timeout / 1000}s)`));
      });

      req.on("error", reject);
      req.write(data);
      req.end();
    });
  }
}
