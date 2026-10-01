import {
  Injectable,
  inject
} from '@angular/core';

import {
  HttpClient
} from '@angular/common/http';

import {
  Observable
} from 'rxjs';
import { tap } from 'rxjs';
import { ClientLogger } from '../../../logging/client-logger';

import {
  PreviewRequest,
  PreviewResponse,
  SendEvent,
  SendRequest,
  SendResponse
} from '../models/send.models';


@Injectable({
  providedIn: 'root'
})
export class SendApiService {

  private readonly http =
    inject(HttpClient);
  private readonly logger = inject(ClientLogger);


  /* =========================================================
     PREVIEW
     ========================================================= */

  preview(
    request: PreviewRequest
  ): Observable<PreviewResponse> {

    this.logger.write('info', 'EmailPreview', 'Email preview requested.');

    return this.http.post<PreviewResponse>(
      '/api/preview',
      request
    );

  }


  /* =========================================================
     SEND
     ========================================================= */

  send(
    request: SendRequest
  ): Observable<SendResponse> {

    this.logger.write('info', 'EmailSend', `Send operation requested for ${request.recipients.length} recipients.`);

    return this.http.post<SendResponse>(
      '/api/send',
      request
    ).pipe(tap(response => this.logger.write('info', 'EmailSend',
      `Send operation ${response.operationId} accepted with ${response.totalRows} recipients.`)));

  }


  /* =========================================================
     SSE EVENTS
     ========================================================= */

  sendEvents(
    operationId: string
  ): Observable<SendEvent> {

    return new Observable<SendEvent>(
      subscriber => {

        const url =
          `/api/send/${operationId}/events`;

        console.log(
          'Opening SSE connection:',
          url
        );


        const eventSource =
          new EventSource(url);
        let operationCompleted = false;
        this.logger.write('debug', 'EmailSendEvents', `Listening for operation ${operationId}.`);


        /*
         * Parse the SSE data payload.
         */
        const handleEvent = (
          event: Event
        ): void => {

          const messageEvent =
            event as MessageEvent<string>;


          console.log(
            'Raw SSE event:',
            messageEvent
          );


          if (!messageEvent.data) {
            return;
          }


          try {

            /*
             * Backend currently serializes its C#
             * properties using PascalCase:
             *
             * OperationId
             * Type
             * RowId
             * Status
             * Errors
             * ProviderMessageId
             * SentRows
             * FailedRows
             * TotalRows
             *
             * Normalize them here so the rest of
             * Angular can use camelCase.
             */
            const backendEvent =
              JSON.parse(
                messageEvent.data
              ) as {
                OperationId: string;
                Type: string;
                RowId: number | null;
                Status: string;
                Errors: string[];
                ProviderMessageId: string | null;
                SentRows: number | null;
                FailedRows: number | null;
                TotalRows: number | null;
              };


            const sendEvent:
              SendEvent = {

                operationId:
                  backendEvent.OperationId,

                type:
                  backendEvent.Type,

                rowId:
                  backendEvent.RowId,

                status:
                  backendEvent.Status,

                errors:
                  backendEvent.Errors ?? [],

                providerMessageId:
                  backendEvent.ProviderMessageId,

                sentRows:
                  backendEvent.SentRows,

                failedRows:
                  backendEvent.FailedRows,

                totalRows:
                  backendEvent.TotalRows

              };

            if (sendEvent.status === 'Completed') {
              operationCompleted = true;
              this.logger.write('info', 'EmailSendEvents',
                `Operation ${operationId} completed: ${sendEvent.sentRows ?? 0} sent, ${sendEvent.failedRows ?? 0} failed, ${sendEvent.totalRows ?? 0} total.`);
            }


            console.log(
              'Parsed SSE event:',
              sendEvent
            );


            subscriber.next(
              sendEvent
            );

          }
          catch (error) {

            console.error(
              'Unable to parse SSE event:',
              messageEvent.data,
              error
            );


            subscriber.error(
              new Error(
                'The server returned an invalid SSE event.'
              )
            );

          }

        };


        /*
         * Your backend sends:
         *
         * event: row-update
         *
         * Event names are case-sensitive.
         */
        eventSource.addEventListener(
          'row-update',
          handleEvent
        );


        /*
         * Also support unnamed SSE messages.
         */
        eventSource.addEventListener(
          'message',
          handleEvent
        );


        eventSource.onerror = (
          error
        ): void => {

          if (operationCompleted) {
            eventSource.close();
            subscriber.complete();
            return;
          }

          console.error(
            'SSE connection error:',
            error
          );


          subscriber.error(
            new Error(
              'The SSE connection was lost.'
            )
          );


          eventSource.close();

        };


        /*
         * Cleanup.
         */
        return () => {

          console.log(
            'Closing SSE connection:',
            url
          );

          eventSource.close();

        };

      }
    );

  }

}
