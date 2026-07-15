import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';
import { ApiService, LawInfo } from '../../services/api';

@Component({
  selector: 'app-laws',
  imports: [TranslatePipe, DatePipe],
  templateUrl: './laws.html',
  styleUrl: './laws.css',
})
export class LawsPage {
  protected readonly laws = signal<LawInfo[] | null>(null);
  protected readonly failed = signal(false);

  constructor() {
    inject(ApiService).laws()
      .then(l => this.laws.set(l))
      .catch(() => this.failed.set(true));
  }
}
