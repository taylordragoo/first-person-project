/**
 * Unity MCP Bridge Extension for pi
 *
 * Connects pi to the Unity MCP server via stdio transport,
 * discovers Unity tools, and registers them as pi tools.
 *
 * Prerequisites:
 *   1. Unity Editor must be open with the MCP for Unity plugin installed
 *   2. In Unity: Window > MCP for Unity > Auto-Setup
 *   3. In Unity: Click "Start Bridge" if not already running
 *   4. uvx must be installed (brew install uv)
 */

import type { ExtensionAPI } from "@earendil-works/pi-coding-agent";
import { Type } from "typebox";
import { spawn, type ChildProcess } from "node:child_process";
import { createInterface } from "node:readline";

// ── MCP JSON-RPC types ──────────────────────────────────────────────

interface JsonRpcRequest {
  jsonrpc: "2.0";
  id: number;
  method: string;
  params?: Record<string, unknown>;
}

interface JsonRpcResponse {
  jsonrpc: "2.0";
  id: number;
  result?: unknown;
  error?: { code: number; message: string; data?: unknown };
}

interface McpToolDef {
  name: string;
  description?: string;
  inputSchema: {
    type: "object";
    properties?: Record<string, {
      type: string;
      description?: string;
      enum?: string[];
      default?: unknown;
    }>;
    required?: string[];
  };
}

// ── MCP Client ──────────────────────────────────────────────────────

class McpClient {
  private proc: ChildProcess | null = null;
  private requestId = 0;
  private pending = new Map<number, {
    resolve: (value: JsonRpcResponse) => void;
    reject: (err: Error) => void;
  }>();
  private buffer = "";
  private tools: McpToolDef[] = [];
  private connected = false;
  private onDisconnect?: () => void;

  async connect(onDisconnect?: () => void): Promise<void> {
    this.onDisconnect = onDisconnect;

    return new Promise((resolve, reject) => {
      // The MCP server is published on PyPI as 'mcpforunityserver'
      // The entry point is 'mcp-for-unity'
      // Use --from to pin the version that matches the Unity plugin
      const args = [
        "--from", "mcpforunityserver==10.1.0",
        "mcp-for-unity",
        "--transport", "stdio",
      ];

      console.error("[unity-mcp] Starting:", "uvx", args.join(" "));

      this.proc = spawn("uvx", args, {
        stdio: ["pipe", "pipe", "pipe"],
        env: { ...process.env },
      });

      const rl = createInterface({ input: this.proc.stdout! });
      let initialized = false;

      rl.on("line", (line: string) => {
        try {
          const msg = JSON.parse(line);
          this.handleMessage(msg);
        } catch {
          // skip non-JSON lines
        }
      });

      this.proc.stderr?.on("data", (data: Buffer) => {
        // MCP server logs to stderr; forward for debugging
        const text = data.toString().trim();
        if (text) {
          console.error("[unity-mcp]", text);
        }
      });

      this.proc.on("close", (code) => {
        this.connected = false;
        if (!initialized) {
          reject(new Error(`MCP server exited with code ${code} before initializing`));
        }
        this.onDisconnect?.();
      });

      this.proc.on("error", (err) => {
        this.connected = false;
        if (!initialized) {
          reject(err);
        }
      });

      // Send initialize request
      this.sendRequest("initialize", {
        protocolVersion: "2024-11-05",
        capabilities: {},
        clientInfo: {
          name: "pi-coding-agent",
          version: "1.0.0",
        },
      }).then((response) => {
        if (response.error) {
          reject(new Error(`MCP initialize failed: ${response.error.message}`));
          return;
        }
        // Send initialized notification
        this.sendNotification("notifications/initialized", {});
        initialized = true;
        this.connected = true;
        resolve();
      }).catch(reject);
    });
  }

  private handleMessage(msg: JsonRpcResponse): void {
    if (msg.id !== undefined && this.pending.has(msg.id)) {
      const { resolve } = this.pending.get(msg.id)!;
      this.pending.delete(msg.id);
      resolve(msg);
    }
  }

  private sendRequest(method: string, params?: Record<string, unknown>): Promise<JsonRpcResponse> {
    const id = ++this.requestId;
    const request: JsonRpcRequest = {
      jsonrpc: "2.0",
      id,
      method,
      params,
    };

    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.proc?.stdin?.write(JSON.stringify(request) + "\n");

      // Timeout after 30 seconds
      setTimeout(() => {
        if (this.pending.has(id)) {
          this.pending.delete(id);
          reject(new Error(`MCP request '${method}' timed out`));
        }
      }, 30000);
    });
  }

  private sendNotification(method: string, params?: Record<string, unknown>): void {
    const notification = {
      jsonrpc: "2.0",
      method,
      params,
    };
    this.proc?.stdin?.write(JSON.stringify(notification) + "\n");
  }

  async listTools(): Promise<McpToolDef[]> {
    const response = await this.sendRequest("tools/list", {});
    if (response.error) {
      throw new Error(`Failed to list tools: ${response.error.message}`);
    }
    const result = response.result as { tools: McpToolDef[] };
    this.tools = result.tools || [];
    return this.tools;
  }

  async callTool(name: string, args: Record<string, unknown>): Promise<unknown> {
    const response = await this.sendRequest("tools/call", {
      name,
      arguments: args,
    });
    if (response.error) {
      throw new Error(`Tool '${name}' failed: ${response.error.message}`);
    }
    return response.result;
  }

  getTools(): McpToolDef[] {
    return this.tools;
  }

  isConnected(): boolean {
    return this.connected;
  }

  async disconnect(): Promise<void> {
    if (this.proc) {
      this.proc.kill();
      this.proc = null;
    }
    this.connected = false;
  }
}

// ── TypeBox schema mapping ──────────────────────────────────────────

function mcpSchemaToTypeBox(tool: McpToolDef) {
  const properties: Record<string, any> = {};
  const required: string[] = [];

  if (tool.inputSchema?.properties) {
    for (const [key, prop] of Object.entries(tool.inputSchema.properties)) {
      let tbType: any;

      switch (prop.type) {
        case "string":
          tbType = Type.String();
          break;
        case "number":
        case "integer":
          tbType = Type.Number();
          break;
        case "boolean":
          tbType = Type.Boolean();
          break;
        case "array":
          tbType = Type.Array(Type.Any());
          break;
        case "object":
          tbType = Type.Object({}, { additionalProperties: true });
          break;
        default:
          tbType = Type.String();
      }

      if (prop.description) {
        tbType = Type.Optional(tbType);
        // TypeBox doesn't have a direct way to add description to the schema
        // but we can add it via the schema options
        tbType = { ...tbType, description: prop.description };
      }

      properties[key] = tbType;
    }

    if (tool.inputSchema.required) {
      required.push(...tool.inputSchema.required);
    }
  }

  return Type.Object(properties, { required });
}

// ── Extension ───────────────────────────────────────────────────────

export default async function (pi: ExtensionAPI) {
  const mcp = new McpClient();
  let toolsRegistered = false;

  // Connect to MCP server on session start
  pi.on("session_start", async (_event, ctx) => {
    if (toolsRegistered) return; // already connected

    try {
      ctx.ui.setStatus("unity-mcp", "Connecting to Unity MCP...");
      await mcp.connect(() => {
        ctx.ui.setStatus("unity-mcp", "Unity MCP disconnected");
        toolsRegistered = false;
      });

      ctx.ui.setStatus("unity-mcp", "Discovering Unity tools...");
      const tools = await mcp.listTools();

      if (tools.length === 0) {
        ctx.ui.setStatus("unity-mcp", "No Unity tools found");
        ctx.ui.notify(
          "Unity MCP: No tools discovered. Make sure Unity Editor is open and the MCP bridge is running.",
          "warn"
        );
        return;
      }

      // Register each Unity tool as a pi tool
      for (const tool of tools) {
        const schema = mcpSchemaToTypeBox(tool);

        pi.registerTool({
          name: `unity_${tool.name}`,
          label: `Unity: ${tool.name}`,
          description: tool.description || `Unity MCP tool: ${tool.name}`,
          parameters: schema,
          async execute(_toolCallId, params) {
            if (!mcp.isConnected()) {
              return {
                content: [{
                  type: "text",
                  text: "Error: Unity MCP is not connected. Make sure Unity Editor is open and the MCP bridge is running.",
                }],
                details: {},
                isError: true,
              };
            }

            try {
              const result = await mcp.callTool(tool.name, params as Record<string, unknown>);
              const resultStr = typeof result === "string"
                ? result
                : JSON.stringify(result, null, 2);

              return {
                content: [{ type: "text", text: resultStr }],
                details: { toolName: tool.name, rawResult: result },
              };
            } catch (err: any) {
              return {
                content: [{ type: "text", text: `Unity tool error: ${err.message}` }],
                details: {},
                isError: true,
              };
            }
          },
        });
      }

      toolsRegistered = true;
      ctx.ui.setStatus("unity-mcp", `Unity MCP: ${tools.length} tools`);
      ctx.ui.notify(
        `Unity MCP connected with ${tools.length} tools from Unity Editor`,
        "info"
      );
    } catch (err: any) {
      ctx.ui.setStatus("unity-mcp", "Unity MCP: connection failed");
      ctx.ui.notify(
        `Unity MCP connection failed: ${err.message}. Is Unity Editor open with the MCP bridge running?`,
        "error"
      );
    }
  });

  // Cleanup on shutdown
  pi.on("session_shutdown", async () => {
    await mcp.disconnect();
    toolsRegistered = false;
  });
}
