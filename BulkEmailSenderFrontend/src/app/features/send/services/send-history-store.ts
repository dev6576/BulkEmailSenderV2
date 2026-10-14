import { Injectable } from '@angular/core';

export interface SendHistoryEntry {
  id: string;
  operationId: string;
  sentAt: string;
  status: string;
  values: Record<string, string>;
  errors: string[];
  template?: {
    id: string;
    name: string;
    updatedAt: number;
  };
}

@Injectable({ providedIn: 'root' })
export class SendHistoryStore {
  private readonly databaseName = 'bulk-email-sender-history';
  private readonly storeName = 'recipients';
  private database?: Promise<IDBDatabase>;

  async list(): Promise<SendHistoryEntry[]> {
    const db = await this.open();
    return new Promise((resolve, reject) => {
      const request = db.transaction(this.storeName, 'readonly').objectStore(this.storeName).getAll();
      request.onsuccess = () => resolve((request.result as SendHistoryEntry[]).sort((a, b) => b.sentAt.localeCompare(a.sentAt)));
      request.onerror = () => reject(request.error);
    });
  }

  async save(entries: SendHistoryEntry[]): Promise<void> {
    const db = await this.open();
    await new Promise<void>((resolve, reject) => {
      const transaction = db.transaction(this.storeName, 'readwrite');
      const store = transaction.objectStore(this.storeName);
      entries.forEach(entry => store.put(entry));
      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error);
      transaction.onabort = () => reject(transaction.error);
    });
  }

  private open(): Promise<IDBDatabase> {
    if (!globalThis.indexedDB) return Promise.reject(new Error('This browser does not support IndexedDB.'));
    this.database ??= new Promise((resolve, reject) => {
      const request = indexedDB.open(this.databaseName, 1);
      request.onupgradeneeded = () => request.result.createObjectStore(this.storeName, { keyPath: 'id' });
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
    return this.database;
  }
}
