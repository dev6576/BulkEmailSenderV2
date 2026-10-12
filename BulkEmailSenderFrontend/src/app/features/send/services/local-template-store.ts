import { Injectable } from '@angular/core';

export interface SavedEmailTemplate {
  id: string;
  name: string;
  subject: string;
  body: string;
  attachments: Array<{ fileName: string; size: number; contentBase64: string }>;
  updatedAt: number;
}

@Injectable({ providedIn: 'root' })
export class LocalTemplateStore {
  private readonly databaseName = 'bulk-email-sender-local';
  private readonly storeName = 'templates';
  private database?: Promise<IDBDatabase>;

  async list(): Promise<SavedEmailTemplate[]> {
    const db = await this.open();
    return new Promise((resolve, reject) => {
      const request = db.transaction(this.storeName, 'readonly').objectStore(this.storeName).getAll();
      request.onsuccess = () => resolve((request.result as SavedEmailTemplate[]).sort((a, b) => a.name.localeCompare(b.name)));
      request.onerror = () => reject(request.error);
    });
  }

  async save(template: SavedEmailTemplate): Promise<void> {
    const db = await this.open();
    await new Promise<void>((resolve, reject) => {
      const transaction = db.transaction(this.storeName, 'readwrite');
      transaction.objectStore(this.storeName).put(template);
      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error);
      transaction.onabort = () => reject(transaction.error);
    });
  }

  async delete(id: string): Promise<void> {
    const db = await this.open();
    await new Promise<void>((resolve, reject) => {
      const transaction = db.transaction(this.storeName, 'readwrite');
      transaction.objectStore(this.storeName).delete(id);
      transaction.oncomplete = () => resolve();
      transaction.onerror = () => reject(transaction.error);
      transaction.onabort = () => reject(transaction.error);
    });
  }

  private open(): Promise<IDBDatabase> {
    if (!globalThis.indexedDB) return Promise.reject(new Error('This browser does not support local template storage.'));
    this.database ??= new Promise((resolve, reject) => {
      const request = indexedDB.open(this.databaseName, 1);
      request.onupgradeneeded = () => request.result.createObjectStore(this.storeName, { keyPath: 'id' });
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
    return this.database;
  }
}
