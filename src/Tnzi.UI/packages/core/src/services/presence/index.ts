/**
 * @tnzi/core/services/presence
 *
 * Presence Service - user online status (manual intent + connection-derived resolution +
 * configurable auto-away). Independent of Chat: apps that only need to see who's online in
 * real time can use this without loading the Chat module. Backend: Tnzi.Identity.Presence.
 */

export * from './types';
export * from './api';
export * from './activity';
