import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api/client';
import type { components } from '@/shared/api/schema';

type ServiceDto = components['schemas']['ServiceResponse'];

/** Service with numeric fields (the generated types allow `number | string`). */
export interface Service {
  id: string;
  name: string;
  description: string | null;
  durationMinutes: number;
  price: number;
  currency: string;
  isActive: boolean;
}

export interface ServiceInput {
  name: string;
  description: string | null;
  durationMinutes: number;
  price: number;
}

const servicesKey = ['services'] as const;

const toService = (dto: ServiceDto): Service => ({
  ...dto,
  durationMinutes: Number(dto.durationMinutes),
  price: Number(dto.price),
});

export function useServices() {
  return useQuery({
    queryKey: servicesKey,
    queryFn: async () => (await unwrap(api.GET('/api/services'))).map(toService),
  });
}

export function useCreateService() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: ServiceInput) => unwrap(api.POST('/api/services', { body: input })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: servicesKey }),
  });
}

export function useUpdateService() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...input }: ServiceInput & { id: string; isActive: boolean }) =>
      unwrap(api.PUT('/api/services/{id}', { params: { path: { id } }, body: input })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: servicesKey }),
  });
}

export function useDeleteService() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => unwrap(api.DELETE('/api/services/{id}', { params: { path: { id } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: servicesKey }),
  });
}
