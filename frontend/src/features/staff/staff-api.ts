import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api/client';
import type { components } from '@/shared/api/schema';

export type Professional = components['schemas']['ProfessionalResponse'];

export interface ProfessionalInput {
  name: string;
  serviceIds: string[];
}

export const professionalsKey = ['professionals'] as const;

export function useProfessionals() {
  return useQuery({
    queryKey: professionalsKey,
    queryFn: () => unwrap(api.GET('/api/professionals')),
  });
}

export function useCreateProfessional() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: ProfessionalInput) => unwrap(api.POST('/api/professionals', { body: input })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: professionalsKey }),
  });
}

export function useUpdateProfessional() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...input }: ProfessionalInput & { id: string; isActive: boolean }) =>
      unwrap(api.PUT('/api/professionals/{id}', { params: { path: { id } }, body: input })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: professionalsKey }),
  });
}

export function useDeleteProfessional() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => unwrap(api.DELETE('/api/professionals/{id}', { params: { path: { id } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: professionalsKey }),
  });
}
