import { Component, OnDestroy, computed, inject, signal } from '@angular/core';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ApiService, AskResponse, RateLimitError, SourceRef } from '../../services/api';
import { LangService } from '../../services/lang';

type ChatState = 'idle' | 'loading' | 'done' | 'error' | 'limited';

/** Этапы скелетона — показываем пайплайн (§9: «вау-момент демо»). */
const STAGES = ['chat.stage.search', 'chat.stage.compose', 'chat.stage.verify'] as const;

@Component({
  selector: 'app-chat',
  imports: [TranslatePipe],
  templateUrl: './chat.html',
  styleUrl: './chat.css',
})
export class ChatPage implements OnDestroy {
  protected readonly api = inject(ApiService);
  protected readonly lang = inject(LangService);
  private readonly translate = inject(TranslateService);

  protected readonly state = signal<ChatState>('idle');
  protected readonly question = signal('');
  protected readonly askedQuestion = signal('');
  protected readonly response = signal<AskResponse | null>(null);
  protected readonly stageIndex = signal(0);
  protected readonly openedSource = signal<number | null>(null);
  protected readonly limitReset = signal<Date | null>(null);
  protected readonly limitCountdown = signal('');

  protected readonly stages = STAGES;

  /** Ответ с кликабельными маркерами-чипами: [1] → кнопка. */
  protected readonly answerParts = computed(() => {
    const r = this.response();
    if (!r) return [];
    return r.answer.split(/(\[\d{1,2}\])/g).map(part => {
      const m = part.match(/^\[(\d{1,2})\]$/);
      return m ? { marker: +m[1] } : { text: part };
    });
  });

  protected readonly suggested = computed(() => {
    this.lang.current(); // подписка на смену языка UI
    return [1, 2, 3, 4].map(i => this.translate.instant(`chat.suggested.q${i}`));
  });

  private stageTimer: ReturnType<typeof setInterval> | null = null;
  private countdownTimer: ReturnType<typeof setInterval> | null = null;

  protected useSuggestion(q: string): void {
    this.question.set(q);
    void this.ask();
  }

  protected onKeydown(e: KeyboardEvent): void {
    if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      void this.ask();
    }
  }

  protected async ask(): Promise<void> {
    const q = this.question().trim();
    if (!q || this.state() === 'loading') return;

    this.askedQuestion.set(q);
    this.state.set('loading');
    this.response.set(null);
    this.openedSource.set(null);
    this.startStages();

    try {
      const resp = await this.api.ask(q);
      this.response.set(resp);
      this.state.set('done');
      this.question.set('');
    } catch (e) {
      if (e instanceof RateLimitError) {
        this.limitReset.set(e.resetAt);
        this.startCountdown();
        this.state.set('limited');
      } else {
        this.state.set('error');
      }
    } finally {
      this.stopStages();
    }
  }

  protected toggleSource(marker: number): void {
    this.openedSource.set(this.openedSource() === marker ? null : marker);
  }

  protected sourceByMarker(marker: number): SourceRef | undefined {
    return this.response()?.sources.find(s => s.marker === marker);
  }

  private startStages(): void {
    this.stageIndex.set(0);
    this.stageTimer = setInterval(() => {
      if (this.stageIndex() < STAGES.length - 1) this.stageIndex.update(i => i + 1);
    }, 2500);
  }

  private stopStages(): void {
    if (this.stageTimer) clearInterval(this.stageTimer);
    this.stageTimer = null;
  }

  private startCountdown(): void {
    const tick = () => {
      const reset = this.limitReset();
      if (!reset) return this.limitCountdown.set('');
      const ms = reset.getTime() - Date.now();
      if (ms <= 0) {
        this.limitCountdown.set('');
        this.state.set('idle');
        if (this.countdownTimer) clearInterval(this.countdownTimer);
        return;
      }
      const h = Math.floor(ms / 3_600_000);
      const m = Math.floor((ms % 3_600_000) / 60_000);
      this.limitCountdown.set(`${h}:${String(m).padStart(2, '0')}`);
    };
    tick();
    this.countdownTimer = setInterval(tick, 30_000);
  }

  ngOnDestroy(): void {
    this.stopStages();
    if (this.countdownTimer) clearInterval(this.countdownTimer);
  }
}
