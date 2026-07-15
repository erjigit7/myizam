import { HttpClient, HttpErrorResponse, HttpResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface SourceRef {
  marker: number;
  chunkId: number;
  law: string;
  article: string | null;
  articleTitle: string | null;
  excerpt: string;
  url: string | null;
}

export interface GuardInfo {
  grounded: boolean;
  score: number;
}

export interface AskResponse {
  answer: string;
  answerLang: string;
  sources: SourceRef[];
  guard: GuardInfo | null;
  disclaimer: string;
  refused: boolean;
}

export interface LawInfo {
  code: string;
  title: string;
  status: string;
  editionDate: string;
  articleCount: number;
  sourceUrl: string | null;
}

export class RateLimitError extends Error {
  constructor(public resetAt: Date | null) {
    super('rate limit');
  }
}

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  /** Остаток дневного лимита — из X-RateLimit-Remaining каждого ответа. */
  readonly remaining = signal<number | null>(null);

  async ask(question: string): Promise<AskResponse> {
    try {
      const resp = await firstValueFrom(
        this.http.post<AskResponse>('/api/ask', { question }, { observe: 'response' }),
      );
      this.readRateHeaders(resp);
      return resp.body!;
    } catch (e) {
      if (e instanceof HttpErrorResponse && e.status === 429) {
        const reset = e.headers.get('X-RateLimit-Reset');
        this.remaining.set(0);
        throw new RateLimitError(reset ? new Date(+reset * 1000) : null);
      }
      throw e;
    }
  }

  laws(): Promise<LawInfo[]> {
    return firstValueFrom(this.http.get<LawInfo[]>('/api/laws'));
  }

  private readRateHeaders(resp: HttpResponse<unknown>): void {
    const remaining = resp.headers.get('X-RateLimit-Remaining');
    if (remaining !== null) this.remaining.set(+remaining);
  }
}
